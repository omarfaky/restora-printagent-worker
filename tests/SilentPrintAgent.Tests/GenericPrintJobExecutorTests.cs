using Microsoft.Extensions.Logging.Abstractions;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;
using SilentPrintAgent.Services;
using SilentPrintAgent.Services.Transports;
using Xunit;

namespace SilentPrintAgent.Tests;

public sealed class GenericPrintJobExecutorTests
{
    private static readonly PrinterProfile Profile = new("receipt", "Receipt", AgentConnectionType.NetworkEscPos,
        null, "127.0.0.1", 9100, 80, null, 1, false, true);

    [Fact]
    public async Task Known_failure_before_send_is_retryable()
    {
        var transport = new FakeTransport { Failure = new PrinterException(PrinterException.Codes.PrinterOffline, "offline") };
        var result = await CreateExecutor(transport).ExecuteAsync(Job(copies: 1), CancellationToken.None);

        Assert.Equal(PrintJobStatus.Retrying, result.Status);
        Assert.Equal(DeliveryState.NotStarted, result.DeliveryState);
        Assert.Equal(1, transport.SendCount);
    }

    [Fact]
    public async Task Uncertain_transport_failure_is_not_retried()
    {
        var transport = new FakeTransport { Failure = new PrinterException("DeliveryUnknown", "connection lost") { DeliveryUncertain = true } };
        var result = await CreateExecutor(transport).ExecuteAsync(Job(copies: 1), CancellationToken.None);

        Assert.Equal(PrintJobStatus.DeliveryUnknown, result.Status);
        Assert.Equal(DeliveryState.Unknown, result.DeliveryState);
    }

    [Fact]
    public async Task Failure_after_a_previous_copy_is_delivery_unknown()
    {
        var transport = new FakeTransport { FailOnSend = 2, Failure = new PrinterException(PrinterException.Codes.PrinterOffline, "offline") };
        var result = await CreateExecutor(transport).ExecuteAsync(Job(copies: 2), CancellationToken.None);

        Assert.Equal(PrintJobStatus.DeliveryUnknown, result.Status);
        Assert.Equal(2, transport.SendCount);
    }

    private static GenericPrintJobExecutor CreateExecutor(FakeTransport transport)
    {
        var resolver = new PrinterTransportResolver([transport], NullLogger<PrinterTransportResolver>.Instance);
        var drawer = new CashDrawerService(resolver);
        return new GenericPrintJobExecutor([new FakeRenderer()], resolver, drawer, NullLogger<GenericPrintJobExecutor>.Instance);
    }

    private static PrintJob Job(byte copies) => new(Guid.NewGuid(), "job", "client", "hash", "receipt", Profile,
        new PrintPayload(PrintPayloadKind.RawEscPos, null, [0x1B, 0x40], "application/octet-stream", copies),
        PrintJobStatus.Printing, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        null, "lease", DateTimeOffset.UtcNow.AddMinutes(1), null, null, null, DeliveryState.NotStarted);

    private sealed class FakeRenderer : IPayloadRenderer
    {
        public bool CanRender(PrintPayloadKind kind) => true;
        public Task<byte[]> RenderAsync(PrintPayload payload, PrinterProfile profile, CancellationToken ct) => Task.FromResult(new byte[] { 1, 2, 3 });
    }

    private sealed class FakeTransport : IPrinterTransport
    {
        public AgentConnectionType ConnectionType => AgentConnectionType.NetworkEscPos;
        public int SendCount { get; private set; }
        public int? FailOnSend { get; init; } = 1;
        public PrinterException? Failure { get; init; }
        public Task SendAsync(AgentPrinterDto printer, byte[] data, CancellationToken ct)
        {
            SendCount++;
            if (Failure is not null && SendCount == FailOnSend) throw Failure;
            return Task.CompletedTask;
        }
    }
}
