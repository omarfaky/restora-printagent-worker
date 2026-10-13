using SilentPrintAgent.Core.Models;

namespace SilentPrintAgent.Core.Contracts;

public interface IPrintJobStore
{
    Task<PrintJob?> GetAsync(string clientId, string jobId, CancellationToken ct);
    Task<PrintJob> EnqueueAsync(PrintSubmission submission, CancellationToken ct);
    Task<(PrintJob? Job, JobLease? Lease)> TryClaimNextAsync(DateTimeOffset now, TimeSpan leaseDuration, CancellationToken ct);
    Task<bool> RenewLeaseAsync(Guid localJobId, string leaseId, DateTimeOffset now, TimeSpan extension, CancellationToken ct);
    Task CommitCompletedAsync(Guid localJobId, string leaseId, CancellationToken ct);
    Task CommitRetryingAsync(Guid localJobId, string leaseId, PrintFailure failure, DateTimeOffset nextAttemptAt, CancellationToken ct);
    Task CommitDeliveryUnknownAsync(Guid localJobId, string leaseId, PrintFailure failure, CancellationToken ct);
    Task CommitFailedAsync(Guid localJobId, string leaseId, PrintFailure failure, CancellationToken ct);
    Task<PrintJob?> TryCancelPendingAsync(string clientId, string jobId, CancellationToken ct);
    Task<int> DeleteExpiredAsync(DateTimeOffset now, TimeSpan completedRetention, TimeSpan deliveryUnknownRetention, CancellationToken ct);
}

public interface IPrintQueue { Task SignalAsync(CancellationToken ct); Task WaitAsync(CancellationToken ct); }
public interface IPrintJobExecutor { Task<PrintResult> ExecuteAsync(PrintJob job, CancellationToken ct); }
public interface IPayloadRenderer { bool CanRender(PrintPayloadKind kind); Task<byte[]> RenderAsync(PrintPayload payload, PrinterProfile profile, CancellationToken ct); }
public interface IPrinterResolver { Task<PrinterProfile?> ResolveAsync(string printerId, CancellationToken ct); Task<IReadOnlyList<PrinterProfile>> ListAsync(CancellationToken ct); }
