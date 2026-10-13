using System.Net;
using System.Text;
using System.Text.Json;
using SilentPrintAgent.CloudAdapterSample;
using Xunit;

namespace SilentPrintAgent.Tests;

public sealed class CloudPrintBridgeTests
{
    [Fact]
    public async Task Bridge_reports_terminal_local_job_outcome_to_backend()
    {
        using var stop = new CancellationTokenSource();
        var backend = new FakeBackend(stop);
        var handler = new FakeAgentHandler();
        using var agent = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5055") };
        var bridge = new CloudPrintBridge(backend, agent, TimeSpan.FromMilliseconds(1), TimeSpan.FromHours(1));

        await bridge.RunAsync(stop.Token);

        Assert.Equal("sale-1001", backend.Reported?.JobId);
        Assert.Equal("Completed", backend.Reported?.Status);
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class FakeBackend(CancellationTokenSource stop) : ICloudPrintBackend
    {
        private bool _claimed;
        public AgentJobStatus? Reported { get; private set; }

        public Task<CloudPrintWorkItem?> ClaimNextAsync(CancellationToken cancellationToken)
        {
            if (_claimed) return Task.FromResult<CloudPrintWorkItem?>(null);
            _claimed = true;
            using var document = JsonDocument.Parse("{\"totalAmount\":1}");
            return Task.FromResult<CloudPrintWorkItem?>(new CloudPrintWorkItem(
                "sale-1001", "receipt", document.RootElement.Clone(), "lease-1"));
        }

        public Task<bool> RenewLeaseAsync(CloudPrintWorkItem workItem, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task ReportOutcomeAsync(CloudPrintWorkItem workItem, AgentJobStatus outcome, CancellationToken cancellationToken)
        {
            Reported = outcome;
            stop.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgentHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var json = request.Method == HttpMethod.Post
                ? "{\"jobId\":\"sale-1001\",\"status\":\"Pending\",\"errorCode\":null,\"errorMessage\":null}"
                : "{\"jobId\":\"sale-1001\",\"status\":\"Completed\",\"errorCode\":null,\"errorMessage\":null}";
            return Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Accepted : HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
