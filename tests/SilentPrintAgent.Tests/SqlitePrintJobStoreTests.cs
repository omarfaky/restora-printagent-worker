using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;
using SilentPrintAgent.Services.Queue;
using Xunit;

namespace SilentPrintAgent.Tests;

public sealed class SqlitePrintJobStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "silent-print-tests", Guid.NewGuid().ToString("N"));
    private readonly SqlitePrintJobStore _store;
    private readonly PrinterProfile _profile = new("receipt", "Receipt", AgentConnectionType.NetworkEscPos,
        null, "127.0.0.1", 9100, 80, null, 1, false, true);

    public SqlitePrintJobStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new SqlitePrintJobStore(Path.Combine(_directory, "jobs.db"));
    }

    [Fact]
    public async Task Enqueue_is_idempotent_for_same_request_and_conflicts_for_changed_request()
    {
        var first = await _store.EnqueueAsync(Submission("job-1", "hash-a"), CancellationToken.None);
        var duplicate = await _store.EnqueueAsync(Submission("job-1", "hash-a"), CancellationToken.None);

        Assert.Equal(first.LocalJobId, duplicate.LocalJobId);
        await Assert.ThrowsAsync<PrintJobConflictException>(() => _store.EnqueueAsync(Submission("job-1", "hash-b"), CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_claims_never_lease_the_same_job_twice()
    {
        await _store.EnqueueAsync(Submission("job-2", "hash-a"), CancellationToken.None);
        var otherStore = new SqlitePrintJobStore(Path.Combine(_directory, "jobs.db"));

        var claims = await Task.WhenAll(
            _store.TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), CancellationToken.None),
            otherStore.TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), CancellationToken.None));

        Assert.Single(claims, x => x.Job is not null && x.Lease is not null);
    }

    [Fact]
    public async Task Expired_printing_lease_becomes_delivery_unknown()
    {
        var enqueued = await _store.EnqueueAsync(Submission("job-3", "hash-a"), CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var claim = await _store.TryClaimNextAsync(now, TimeSpan.FromSeconds(-1), CancellationToken.None);
        Assert.NotNull(claim.Lease);

        var reclaimed = await _store.TryClaimNextAsync(now.AddSeconds(2), TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Null(reclaimed.Job);
        var stored = await _store.GetAsync("client", "job-3", CancellationToken.None);
        Assert.Equal(enqueued.LocalJobId, stored!.LocalJobId);
        Assert.Equal(PrintJobStatus.DeliveryUnknown, stored.Status);
        Assert.Equal(DeliveryState.Unknown, stored.DeliveryState);
    }

    [Fact]
    public async Task Cancel_only_changes_pending_or_retrying_jobs()
    {
        await _store.EnqueueAsync(Submission("job-4", "hash-a"), CancellationToken.None);
        var cancelled = await _store.TryCancelPendingAsync("client", "job-4", CancellationToken.None);
        Assert.Equal(PrintJobStatus.Cancelled, cancelled!.Status);
        var claim = await _store.TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.Null(claim.Job);
    }

    [Fact]
    public async Task Retention_keeps_unknown_delivery_longer_than_completed_jobs()
    {
        await _store.EnqueueAsync(Submission("job-complete", "hash-a"), CancellationToken.None);
        var completeClaim = await _store.TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);
        await _store.CommitCompletedAsync(completeClaim.Lease!.LocalJobId, completeClaim.Lease.LeaseId, CancellationToken.None);

        await _store.EnqueueAsync(Submission("job-unknown", "hash-b"), CancellationToken.None);
        var unknownClaim = await _store.TryClaimNextAsync(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), CancellationToken.None);
        await _store.CommitDeliveryUnknownAsync(unknownClaim.Lease!.LocalJobId, unknownClaim.Lease.LeaseId,
            new PrintFailure("DeliveryUnknown", "uncertain", false, true), CancellationToken.None);

        var now = DateTimeOffset.UtcNow.AddDays(8);
        Assert.Equal(1, await _store.DeleteExpiredAsync(now, TimeSpan.FromDays(7), TimeSpan.FromDays(30), CancellationToken.None));
        Assert.Null(await _store.GetAsync("client", "job-complete", CancellationToken.None));
        Assert.Equal(PrintJobStatus.DeliveryUnknown, (await _store.GetAsync("client", "job-unknown", CancellationToken.None))!.Status);
    }

    private PrintSubmission Submission(string jobId, string hash) => new(jobId, "client", hash, "receipt", _profile,
        new PrintPayload(PrintPayloadKind.RawEscPos, null, [0x1B, 0x40], "application/octet-stream"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
