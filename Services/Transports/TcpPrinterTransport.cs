using System.Net.Sockets;
using SilentPrintAgent.Models;

namespace SilentPrintAgent.Services.Transports;

/// <summary>
/// يرسل ESC/POS bytes لطابعة شبكة عبر TCP socket، عادة على port 9100.
/// </summary>
public sealed class TcpPrinterTransport(ILogger<TcpPrinterTransport> logger) : IPrinterTransport
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

    public AgentConnectionType ConnectionType => AgentConnectionType.NetworkEscPos;

    /// <summary>يتصل بالطابعة ويرسل الـ bytes؛ يرمي PrinterException بكود واضح عند الفشل.</summary>
    public async Task SendAsync(AgentPrinterDto printer, byte[] data, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(printer.Address))
            throw new PrinterException(
                PrinterException.Codes.PrinterNotConfigured,
                "عنوان الطابعة غير مضبوط");

        var port = printer.Port ?? 9100;

        var sendStarted = false;
        var phase = TransportSendPhase.Connecting;
        try
        {
            using var client = new TcpClient();

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ConnectTimeout);

            try
            {
                await client.ConnectAsync(printer.Address, port, connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PrinterException(
                    PrinterException.Codes.ConnectionTimeout,
                    $"انتهت مهلة الاتصال بالطابعة {printer.Address}:{port}")
                { Phase = TransportSendPhase.Connecting };
            }

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(SendTimeout);

            var stream = client.GetStream();
            sendStarted = true;
            phase = TransportSendPhase.Sending;
            await stream.WriteAsync(data, sendCts.Token);
            await stream.FlushAsync(sendCts.Token);
            phase = TransportSendPhase.SendCompleted;

            logger.LogInformation(
                "أرسلت {ByteCount} بايت إلى الطابعة {Address}:{Port}",
                data.Length, printer.Address, port);
        }
        catch (PrinterException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PrinterException(
                sendStarted ? "DeliveryUnknown" : PrinterException.Codes.ConnectionTimeout,
                sendStarted ? "انقطعت المهلة بعد بدء إرسال البيانات للطابعة." : $"انتهت مهلة الاتصال بالطابعة {printer.Address}:{port}", ex)
            { DeliveryUncertain = sendStarted, Phase = phase };
        }
        catch (SocketException ex)
        {
            throw new PrinterException(
                sendStarted ? "DeliveryUnknown" : PrinterException.Codes.PrinterOffline,
                sendStarted ? "انقطع الاتصال بعد بدء إرسال البيانات للطابعة." : $"تعذر الوصول للطابعة {printer.Address}:{port} - {ex.SocketErrorCode}", ex)
            { DeliveryUncertain = sendStarted, Phase = phase };
        }
        catch (Exception ex)
        {
            throw new PrinterException(
                sendStarted ? "DeliveryUnknown" : PrinterException.Codes.Unknown,
                sendStarted ? "حدث خطأ بعد بدء إرسال البيانات للطابعة." : $"خطأ غير متوقع أثناء الطباعة: {ex.Message}", ex)
            { DeliveryUncertain = sendStarted, Phase = phase };
        }
    }
}
