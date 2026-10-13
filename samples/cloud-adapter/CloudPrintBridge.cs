using System.Net.Http.Json;
using System.Text.Json;

namespace SilentPrintAgent.CloudAdapterSample;

/// <summary>
/// Provider-specific code implements these three operations using its own API and credentials.
/// ReportOutcomeAsync should be idempotent for a given StableJobId.
/// </summary>
public interface ICloudPrintBackend
{
    Task<CloudPrintWorkItem?> ClaimNextAsync(CancellationToken cancellationToken);
    Task<bool> RenewLeaseAsync(CloudPrintWorkItem workItem, CancellationToken cancellationToken);
    Task ReportOutcomeAsync(CloudPrintWorkItem workItem, AgentJobStatus outcome, CancellationToken cancellationToken);
}

public sealed record CloudPrintWorkItem(
    string StableJobId,
    string PrinterId,
    JsonElement Receipt,
    string BackendLeaseToken);

public sealed record AgentJobStatus(
    string JobId,
    string Status,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Provider-neutral bridge logic. Configure the supplied HttpClient for
/// http://127.0.0.1:5055 and add X-Agent-Token from protected local settings.
/// </summary>
public sealed class CloudPrintBridge(
    ICloudPrintBackend backend,
    HttpClient agent,
    TimeSpan? pollInterval = null,
    TimeSpan? leaseRenewalInterval = null,
    Action<Exception>? onError = null)
{
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    private readonly TimeSpan _leaseRenewalInterval = leaseRenewalInterval ?? TimeSpan.FromSeconds(20);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var work = await backend.ClaimNextAsync(cancellationToken);
                if (work is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                await ProcessAsync(work, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed claim/report is retried by the next loop. The backend should
                // make claims and outcome reports safe to repeat using its stable job ID.
                onError?.Invoke(ex);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private async Task ProcessAsync(CloudPrintWorkItem work, CancellationToken cancellationToken)
    {
        using var submit = await agent.PostAsJsonAsync("/api/v1/print/receipt", new
        {
            jobId = work.StableJobId,
            printerId = work.PrinterId,
            receipt = work.Receipt
        }, cancellationToken);
        submit.EnsureSuccessStatusCode();

        var current = await submit.Content.ReadFromJsonAsync<AgentJobStatus>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The Agent returned an empty submit response.");
        var renewAt = DateTimeOffset.UtcNow.Add(_leaseRenewalInterval);

        while (!IsTerminal(current.Status))
        {
            await Task.Delay(_pollInterval, cancellationToken);
            if (DateTimeOffset.UtcNow >= renewAt)
            {
                if (!await backend.RenewLeaseAsync(work, cancellationToken))
                    throw new InvalidOperationException("The backend lease was lost; the local job may still be running.");
                renewAt = DateTimeOffset.UtcNow.Add(_leaseRenewalInterval);
            }

            current = await agent.GetFromJsonAsync<AgentJobStatus>(
                $"/api/v1/jobs/{Uri.EscapeDataString(work.StableJobId)}", cancellationToken)
                ?? throw new InvalidOperationException("The Agent returned an empty job status.");
        }

        await backend.ReportOutcomeAsync(work, current, cancellationToken);
    }

    private static bool IsTerminal(string status) => status is "Completed" or "Failed" or "Cancelled" or "DeliveryUnknown";
}
