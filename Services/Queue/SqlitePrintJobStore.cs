using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;

namespace SilentPrintAgent.Services.Queue;

/// <summary>Durable local queue. Payloads are never written to logs.</summary>
public sealed class SqlitePrintJobStore : IPrintJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _connectionString;

    public SqlitePrintJobStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
        Initialize();
    }

    public async Task<PrintJob?> GetAsync(string clientId, string jobId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM PrintJobs WHERE ClientId=$client AND JobId=$job";
        command.Parameters.AddWithValue("$client", clientId);
        command.Parameters.AddWithValue("$job", jobId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }

    public async Task<PrintJob> EnqueueAsync(PrintSubmission submission, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        var existing = await GetInTransactionAsync(connection, transaction, submission.ClientId, submission.JobId, ct);
        if (existing is not null)
        {
            await transaction.CommitAsync(ct);
            if (!string.Equals(existing.RequestHash, submission.RequestHash, StringComparison.Ordinal))
                throw new PrintJobConflictException(submission.JobId);
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob(Guid.NewGuid(), submission.JobId, submission.ClientId, submission.RequestHash,
            submission.PrinterId, submission.PrinterSnapshot, submission.Payload, PrintJobStatus.Pending, 0,
            now, now, null, now, null, null, null, null, null, DeliveryState.NotStarted);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"INSERT INTO PrintJobs (LocalJobId,ClientId,JobId,RequestHash,PrinterId,ProfileJson,PayloadJson,Status,AttemptCount,CreatedAt,UpdatedAt,StartedAt,NextAttemptAt,ActiveLeaseId,LeaseExpiresAt,CompletedAt,ErrorCode,ErrorMessage,DeliveryState)
VALUES ($id,$client,$job,$hash,$printer,$profile,$payload,$status,$attempts,$created,$updated,NULL,$next,NULL,NULL,NULL,NULL,NULL,$delivery)";
        BindJob(command, job);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return job;
    }

    public async Task<(PrintJob? Job, JobLease? Lease)> TryClaimNextAsync(DateTimeOffset now, TimeSpan leaseDuration, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await using (var recover = connection.CreateCommand())
        {
            recover.Transaction = transaction;
            recover.CommandText = @"UPDATE PrintJobs SET Status=$unknown, DeliveryState=$delivery, UpdatedAt=$now, ErrorCode='LeaseExpired', ErrorMessage='The service stopped while delivery was in progress.', ActiveLeaseId=NULL, LeaseExpiresAt=NULL
WHERE Status=$printing AND LeaseExpiresAt < $now";
            recover.Parameters.AddWithValue("$unknown", (int)PrintJobStatus.DeliveryUnknown);
            recover.Parameters.AddWithValue("$delivery", (int)DeliveryState.Unknown);
            recover.Parameters.AddWithValue("$printing", (int)PrintJobStatus.Printing);
            recover.Parameters.AddWithValue("$now", Stamp(now));
            await recover.ExecuteNonQueryAsync(ct);
        }
        PrintJob? job;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = @"SELECT * FROM PrintJobs WHERE (Status=$pending OR Status=$retrying) AND (NextAttemptAt IS NULL OR NextAttemptAt <= $now) ORDER BY CreatedAt LIMIT 1";
            select.Parameters.AddWithValue("$pending", (int)PrintJobStatus.Pending);
            select.Parameters.AddWithValue("$retrying", (int)PrintJobStatus.Retrying);
            select.Parameters.AddWithValue("$now", Stamp(now));
            await using var reader = await select.ExecuteReaderAsync(ct);
            job = await reader.ReadAsync(ct) ? ReadJob(reader) : null;
        }
        if (job is null) { await transaction.CommitAsync(ct); return (null, null); }

        var lease = new JobLease(job.LocalJobId, Guid.NewGuid().ToString("N"), now.Add(leaseDuration));
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = @"UPDATE PrintJobs SET Status=$status, AttemptCount=AttemptCount+1, StartedAt=COALESCE(StartedAt,$now), UpdatedAt=$now, ActiveLeaseId=$lease, LeaseExpiresAt=$expires, ErrorCode=NULL, ErrorMessage=NULL WHERE LocalJobId=$id";
            update.Parameters.AddWithValue("$status", (int)PrintJobStatus.Printing);
            update.Parameters.AddWithValue("$now", Stamp(now)); update.Parameters.AddWithValue("$lease", lease.LeaseId);
            update.Parameters.AddWithValue("$expires", Stamp(lease.ExpiresAt)); update.Parameters.AddWithValue("$id", job.LocalJobId.ToString("D"));
            await update.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return (job with { Status = PrintJobStatus.Printing, AttemptCount = job.AttemptCount + 1, StartedAt = job.StartedAt ?? now, UpdatedAt = now, ActiveLeaseId = lease.LeaseId, LeaseExpiresAt = lease.ExpiresAt }, lease);
    }

    public async Task<bool> RenewLeaseAsync(Guid localJobId, string leaseId, DateTimeOffset now, TimeSpan extension, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PrintJobs SET LeaseExpiresAt=$expires, UpdatedAt=$now WHERE LocalJobId=$id AND ActiveLeaseId=$lease AND Status=$printing";
        command.Parameters.AddWithValue("$expires", Stamp(now.Add(extension)));
        command.Parameters.AddWithValue("$now", Stamp(now));
        command.Parameters.AddWithValue("$id", localJobId.ToString("D"));
        command.Parameters.AddWithValue("$lease", leaseId);
        command.Parameters.AddWithValue("$printing", (int)PrintJobStatus.Printing);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public Task CommitCompletedAsync(Guid id, string lease, CancellationToken ct) => CommitAsync(id, lease, PrintJobStatus.Completed, DeliveryState.Acknowledged, null, null, null, ct);
    public Task CommitDeliveryUnknownAsync(Guid id, string lease, PrintFailure failure, CancellationToken ct) => CommitAsync(id, lease, PrintJobStatus.DeliveryUnknown, DeliveryState.Unknown, failure, null, null, ct);
    public Task CommitFailedAsync(Guid id, string lease, PrintFailure failure, CancellationToken ct) => CommitAsync(id, lease, PrintJobStatus.Failed, DeliveryState.NotStarted, failure, null, null, ct);
    public Task CommitRetryingAsync(Guid id, string lease, PrintFailure failure, DateTimeOffset next, CancellationToken ct) => CommitAsync(id, lease, PrintJobStatus.Retrying, DeliveryState.NotStarted, failure, next, null, ct);

    public async Task<PrintJob?> TryCancelPendingAsync(string clientId, string jobId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        var job = await GetInTransactionAsync(connection, null, clientId, jobId, ct);
        if (job is null || job.Status is not (PrintJobStatus.Pending or PrintJobStatus.Retrying)) return null;
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PrintJobs SET Status=$status, UpdatedAt=$now, ErrorCode='Cancelled', ErrorMessage='Cancelled by client' WHERE LocalJobId=$id AND (Status=$pending OR Status=$retrying)";
        command.Parameters.AddWithValue("$status", (int)PrintJobStatus.Cancelled); command.Parameters.AddWithValue("$now", Stamp(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$id", job.LocalJobId.ToString("D")); command.Parameters.AddWithValue("$pending", (int)PrintJobStatus.Pending); command.Parameters.AddWithValue("$retrying", (int)PrintJobStatus.Retrying);
        return await command.ExecuteNonQueryAsync(ct) == 1 ? await GetAsync(clientId, jobId, ct) : null;
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, TimeSpan completedRetention, TimeSpan deliveryUnknownRetention, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = @"DELETE FROM PrintJobs
WHERE (Status IN ($completed,$failed,$cancelled) AND UpdatedAt < $terminalCutoff)
   OR (Status=$unknown AND UpdatedAt < $unknownCutoff)";
        command.Parameters.AddWithValue("$completed", (int)PrintJobStatus.Completed);
        command.Parameters.AddWithValue("$failed", (int)PrintJobStatus.Failed);
        command.Parameters.AddWithValue("$cancelled", (int)PrintJobStatus.Cancelled);
        command.Parameters.AddWithValue("$unknown", (int)PrintJobStatus.DeliveryUnknown);
        command.Parameters.AddWithValue("$terminalCutoff", Stamp(now.Subtract(completedRetention)));
        command.Parameters.AddWithValue("$unknownCutoff", Stamp(now.Subtract(deliveryUnknownRetention)));
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async Task CommitAsync(Guid id, string lease, PrintJobStatus status, DeliveryState delivery, PrintFailure? failure, DateTimeOffset? next, DateTimeOffset? completed, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct); await using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE PrintJobs SET Status=$status, DeliveryState=$delivery, UpdatedAt=$now, NextAttemptAt=$next, CompletedAt=$completed, ErrorCode=$code, ErrorMessage=$message, ActiveLeaseId=NULL, LeaseExpiresAt=NULL WHERE LocalJobId=$id AND ActiveLeaseId=$lease AND Status=$printing";
        command.Parameters.AddWithValue("$status", (int)status); command.Parameters.AddWithValue("$delivery", (int)delivery); command.Parameters.AddWithValue("$now", Stamp(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$next", next is null ? DBNull.Value : Stamp(next.Value)); command.Parameters.AddWithValue("$completed", status == PrintJobStatus.Completed ? Stamp(DateTimeOffset.UtcNow) : DBNull.Value);
        command.Parameters.AddWithValue("$code", (object?)failure?.Code ?? DBNull.Value); command.Parameters.AddWithValue("$message", (object?)failure?.Message ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id.ToString("D")); command.Parameters.AddWithValue("$lease", lease); command.Parameters.AddWithValue("$printing", (int)PrintJobStatus.Printing);
        await command.ExecuteNonQueryAsync(ct);
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;
CREATE TABLE IF NOT EXISTS PrintJobs (LocalJobId TEXT PRIMARY KEY, ClientId TEXT NOT NULL, JobId TEXT NOT NULL, RequestHash TEXT NOT NULL, PrinterId TEXT NOT NULL, ProfileJson TEXT NOT NULL, PayloadJson TEXT NOT NULL, Status INTEGER NOT NULL, AttemptCount INTEGER NOT NULL, CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL, StartedAt TEXT NULL, NextAttemptAt TEXT NULL, ActiveLeaseId TEXT NULL, LeaseExpiresAt TEXT NULL, CompletedAt TEXT NULL, ErrorCode TEXT NULL, ErrorMessage TEXT NULL, DeliveryState INTEGER NOT NULL, UNIQUE(ClientId, JobId));
CREATE INDEX IF NOT EXISTS IX_PrintJobs_Claim ON PrintJobs(Status, NextAttemptAt, CreatedAt);";
        command.ExecuteNonQuery();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct) { var c = new SqliteConnection(_connectionString); await c.OpenAsync(ct); return c; }
    private static async Task<PrintJob?> GetInTransactionAsync(SqliteConnection c, SqliteTransaction? tx, string client, string job, CancellationToken ct)
    {
        await using var command = c.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT * FROM PrintJobs WHERE ClientId=$client AND JobId=$job";
        command.Parameters.AddWithValue("$client", client); command.Parameters.AddWithValue("$job", job); await using var reader = await command.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadJob(reader) : null;
    }
    private static PrintJob ReadJob(SqliteDataReader r) => new(Guid.Parse(r.GetString(r.GetOrdinal("LocalJobId"))), r.GetString(r.GetOrdinal("JobId")), r.GetString(r.GetOrdinal("ClientId")), r.GetString(r.GetOrdinal("RequestHash")), r.GetString(r.GetOrdinal("PrinterId")), JsonSerializer.Deserialize<PrinterProfile>(r.GetString(r.GetOrdinal("ProfileJson")), JsonOptions)!, JsonSerializer.Deserialize<PrintPayload>(r.GetString(r.GetOrdinal("PayloadJson")), JsonOptions)!, (PrintJobStatus)r.GetInt32(r.GetOrdinal("Status")), r.GetInt32(r.GetOrdinal("AttemptCount")), Parse(r, "CreatedAt")!.Value, Parse(r, "UpdatedAt")!.Value, Parse(r, "StartedAt"), Parse(r, "NextAttemptAt"), Value(r, "ActiveLeaseId"), Parse(r, "LeaseExpiresAt"), Parse(r, "CompletedAt"), Value(r, "ErrorCode"), Value(r, "ErrorMessage"), (DeliveryState)r.GetInt32(r.GetOrdinal("DeliveryState")));
    private static void BindJob(SqliteCommand c, PrintJob j) { c.Parameters.AddWithValue("$id", j.LocalJobId.ToString("D")); c.Parameters.AddWithValue("$client", j.ClientId); c.Parameters.AddWithValue("$job", j.JobId); c.Parameters.AddWithValue("$hash", j.RequestHash); c.Parameters.AddWithValue("$printer", j.PrinterId); c.Parameters.AddWithValue("$profile", JsonSerializer.Serialize(j.PrinterSnapshot, JsonOptions)); c.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(j.Payload, JsonOptions)); c.Parameters.AddWithValue("$status", (int)j.Status); c.Parameters.AddWithValue("$attempts", j.AttemptCount); c.Parameters.AddWithValue("$created", Stamp(j.CreatedAt)); c.Parameters.AddWithValue("$updated", Stamp(j.UpdatedAt)); c.Parameters.AddWithValue("$next", j.NextAttemptAt is null ? DBNull.Value : Stamp(j.NextAttemptAt.Value)); c.Parameters.AddWithValue("$delivery", (int)j.DeliveryState); }
    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset? Parse(SqliteDataReader r, string n) => r.IsDBNull(r.GetOrdinal(n)) ? null : DateTimeOffset.Parse(r.GetString(r.GetOrdinal(n)), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string? Value(SqliteDataReader r, string n) => r.IsDBNull(r.GetOrdinal(n)) ? null : r.GetString(r.GetOrdinal(n));
}
