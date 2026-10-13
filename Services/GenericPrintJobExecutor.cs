using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;
using SilentPrintAgent.Services.Rendering;
using SilentPrintAgent.Services.Transports;

namespace SilentPrintAgent.Services;

public sealed class GenericPrintJobExecutor(
    IEnumerable<IPayloadRenderer> renderers,
    PrinterTransportResolver transports,
    CashDrawerService drawer,
    ILogger<GenericPrintJobExecutor> logger) : IPrintJobExecutor
{
    public async Task<PrintResult> ExecuteAsync(PrintJob job, CancellationToken ct)
    {
        var deliveryStarted = false;
        var printedCopies = 0;
        try
        {
            var renderer = renderers.FirstOrDefault(x => x.CanRender(job.Payload.Kind))
                ?? throw new PrinterException("UnsupportedPayload", "Unsupported print payload.");
            var bytes = await renderer.RenderAsync(job.Payload, job.PrinterSnapshot, ct);
            var printer = job.PrinterSnapshot.ToLegacy(job.Payload.Copies);
            if (job.Payload.OpenCashDrawer && !job.PrinterSnapshot.CashDrawerEnabled)
                throw new PrinterException("CashDrawerDisabled", "Cash drawer is not enabled for this printer.");
            for (var copy = 0; copy < job.Payload.Copies; copy++)
            {
                deliveryStarted = true;
                await transports.SendAsync(printer, bytes, ct);
                printedCopies++;
            }
            if (job.Payload.OpenCashDrawer)
            {
                deliveryStarted = true;
                await drawer.OpenAsync(printer, ct);
            }
            logger.LogInformation("Completed local print job {JobId} on {PrinterId} ({Bytes} bytes x {Copies})", job.JobId, job.PrinterId, bytes.Length, job.Payload.Copies);
            return new PrintResult(job.JobId, PrintJobStatus.Completed, DeliveryState.Acknowledged);
        }
        catch (PrinterException ex)
        {
            var uncertain = ex.DeliveryUncertain || printedCopies > 0;
            var retryable = !uncertain && ex.ErrorCode is PrinterException.Codes.PrinterOffline or PrinterException.Codes.ConnectionTimeout;
            return new PrintResult(job.JobId, uncertain ? PrintJobStatus.DeliveryUnknown : retryable ? PrintJobStatus.Retrying : PrintJobStatus.Failed,
                uncertain ? DeliveryState.Unknown : DeliveryState.NotStarted, ex.ErrorCode, ex.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new PrintResult(job.JobId, deliveryStarted ? PrintJobStatus.DeliveryUnknown : PrintJobStatus.Failed,
                deliveryStarted ? DeliveryState.Unknown : DeliveryState.NotStarted, deliveryStarted ? "DeliveryUnknown" : "ExecutionCancelled", "Print execution was interrupted.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var uncertain = deliveryStarted || printedCopies > 0;
            return new PrintResult(job.JobId, uncertain ? PrintJobStatus.DeliveryUnknown : PrintJobStatus.Failed,
                uncertain ? DeliveryState.Unknown : DeliveryState.NotStarted, uncertain ? "DeliveryUnknown" : PrinterException.Codes.Unknown, "Unexpected print failure.");
        }
    }
}
