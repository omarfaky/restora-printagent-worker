using SilentPrintAgent.Configuration;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;

namespace SilentPrintAgent.Workers;

public sealed class PrintQueueWorker(IPrintJobStore store, IPrintQueue queue, IPrintJobExecutor executor, PrintAgentOptions options, ILogger<PrintQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var leaseDuration = TimeSpan.FromSeconds(Math.Max(10, options.LeaseDurationSeconds));
        var nextRetentionSweep = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (now >= nextRetentionSweep)
                {
                    await store.DeleteExpiredAsync(now, TimeSpan.FromDays(Math.Max(1, options.CompletedRetentionDays)),
                        TimeSpan.FromDays(Math.Max(1, options.DeliveryUnknownRetentionDays)), stoppingToken);
                    nextRetentionSweep = now.AddHours(1);
                }
                var (job, lease) = await store.TryClaimNextAsync(now, leaseDuration, stoppingToken);
                if (job is null || lease is null)
                {
                    // The timeout is the normal polling fallback, not an error condition.
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    waitCts.CancelAfter(TimeSpan.FromSeconds(1));
                    try { await queue.WaitAsync(waitCts.Token); }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
                    continue;
                }
                using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var renewalLost = false;
                var renewer = RenewLeaseAsync(job.LocalJobId, lease.LeaseId, leaseDuration, renewalCts.Token,
                    () => { renewalLost = true; executionCts.Cancel(); });
                PrintResult result;
                try { result = await executor.ExecuteAsync(job, executionCts.Token); }
                finally { renewalCts.Cancel(); }
                try { await renewer; } catch (OperationCanceledException) when (renewalCts.IsCancellationRequested) { }
                if (renewalLost)
                    result = new PrintResult(job.JobId, PrintJobStatus.DeliveryUnknown, DeliveryState.Unknown, "LeaseRenewalFailed", "The job lease could not be renewed during execution.");
                if (result.Status == PrintJobStatus.Completed) await store.CommitCompletedAsync(job.LocalJobId, lease.LeaseId, stoppingToken);
                else if (result.Status == PrintJobStatus.DeliveryUnknown)
                    await store.CommitDeliveryUnknownAsync(job.LocalJobId, lease.LeaseId,
                        new PrintFailure(result.ErrorCode ?? "DeliveryUnknown", result.ErrorMessage ?? "Delivery outcome is unknown.", false, true), stoppingToken);
                else
                {
                    var failure = new PrintFailure(result.ErrorCode ?? "PrintFailed", result.ErrorMessage ?? "Print failed.", result.Status == PrintJobStatus.Retrying, false);
                    if (result.Status == PrintJobStatus.Retrying && job.AttemptCount < options.MaxAttempts)
                    {
                        var delay = Math.Min(options.RetryMaxDelaySeconds, options.RetryInitialDelaySeconds * Math.Pow(2, Math.Max(0, job.AttemptCount - 1)));
                        await store.CommitRetryingAsync(job.LocalJobId, lease.LeaseId, failure, DateTimeOffset.UtcNow.AddSeconds(delay), stoppingToken);
                    }
                    else await store.CommitFailedAsync(job.LocalJobId, lease.LeaseId, failure, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Local print queue cycle failed"); await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
        }
    }

    private async Task RenewLeaseAsync(Guid localJobId, string leaseId, TimeSpan leaseDuration, CancellationToken ct, Action onLost)
    {
        var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(1).Ticks, leaseDuration.Ticks / 3));
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            if (!await store.RenewLeaseAsync(localJobId, leaseId, DateTimeOffset.UtcNow, leaseDuration, ct))
            {
                onLost();
                return;
            }
        }
    }
}
