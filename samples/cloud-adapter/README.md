# Cloud adapter starter

This folder is a buildable starter library for the project that adopts Silent Print Agent. It intentionally has no provider SDK, endpoint, account, secret, or project-specific code. Implement `ICloudPrintBackend` with the cloud service you own, register/configure `CloudPrintBridge`, and map its job shape to the local request. The included bridge currently handles structured JSON receipts; add image/raw mapping if your backend needs those payload types.

The bridge runs on the print workstation because a remote cloud server cannot call that workstation's loopback address. Its flow is:

```text
your cloud: claim + lease
       -> workstation bridge: map job
       -> 127.0.0.1:5055: submit with X-Agent-Token
       -> poll local job until terminal
       -> your cloud: report result
```

## Backend-specific pieces to implement

- `ClaimNextAsync`: claim one job and return a stable cloud job ID plus a lease/receipt token.
- `RenewLeaseAsync`: keep the lease valid while the local job is pending or printing, if your cloud uses leases.
- `ReportOutcomeAsync`: idempotently report `Completed`, `Failed`, or `DeliveryUnknown` to your cloud.

Use the stable cloud job ID as the local `jobId` (or a deterministic namespaced equivalent). A retry after a timeout must reuse the same ID and identical content. Continue renewing the lease until the Agent reaches a terminal state. Never translate `202 Accepted` into cloud `Completed`; it only means the local queue accepted the job.

## Local API client example

The following is the provider-independent part to embed in your bridge. `CloudJob` and its backend methods are deliberately left to the adopting project because their shape and authentication are provider-specific.

```csharp
using System.Net.Http.Json;
using System.Text.Json;

public sealed record CloudJob(string StableJobId, string PrinterId, JsonElement Receipt);
public sealed record AgentJobResponse(string JobId, string Status, string? ErrorCode, string? ErrorMessage);

public sealed class SilentPrintAgentClient(HttpClient http)
{
    public async Task<AgentJobResponse> SubmitAndWaitAsync(CloudJob job, CancellationToken ct)
    {
        var submit = await http.PostAsJsonAsync("/api/v1/print/receipt", new
        {
            jobId = job.StableJobId,
            printerId = job.PrinterId,
            receipt = job.Receipt
        }, ct);
        submit.EnsureSuccessStatusCode();

        var queued = await submit.Content.ReadFromJsonAsync<AgentJobResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Agent returned an empty submit response.");

        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            var current = await http.GetFromJsonAsync<AgentJobResponse>(
                $"/api/v1/jobs/{Uri.EscapeDataString(queued.JobId)}", ct)
                ?? throw new InvalidOperationException("Agent returned an empty status response.");

            if (current.Status is "Completed" or "Failed" or "Cancelled" or "DeliveryUnknown")
                return current;
        }
    }
}
```

Configure the `HttpClient` base address as `http://127.0.0.1:5055` and add `X-Agent-Token` from the protected local configuration. For image/raw jobs, use the multipart formats in `docs/API.md`. Add timeout handling around submit/status requests: retry with the same job ID and same content, then reconcile with the local status endpoint before claiming a replacement job.

## Result mapping

| Agent state | Cloud action |
|---|---|
| `Completed` | Report transport acknowledgement; do not claim physical-paper confirmation. |
| `Failed` | Report failure and the returned error code/message. |
| `DeliveryUnknown` | Report uncertainty for reconciliation/manual policy; do not automatically submit a new ID. |
| `Pending`, `Retrying`, `Printing` | Keep the cloud lease alive and continue polling. |

Protect the settings file: every application holding the shared local token has the same print permissions. CORS is not authentication, and the token must not be bundled into a publicly served frontend.

Build this starter with `dotnet build samples/cloud-adapter/SilentPrintAgent.CloudAdapter.csproj`. The consuming application is responsible for starting `CloudPrintBridge.RunAsync`, supplying a cancellation token, and logging exceptions through its `onError` callback.
