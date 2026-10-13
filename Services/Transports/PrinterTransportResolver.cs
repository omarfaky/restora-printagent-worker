using SilentPrintAgent.Models;

namespace SilentPrintAgent.Services.Transports;

/// <summary>
/// يختار الـ transport المناسب حسب نوع اتصال الطابعة.
/// هو نقطة الإرسال الوحيدة التي يعرفها باقي النظام؛ لا يستدعي أحد الـ transports مباشرة.
/// </summary>
public sealed class PrinterTransportResolver
{
    private readonly Dictionary<AgentConnectionType, IPrinterTransport> _transports;
    private readonly ILogger<PrinterTransportResolver> _logger;

    public PrinterTransportResolver(
        IEnumerable<IPrinterTransport> transports,
        ILogger<PrinterTransportResolver> logger)
    {
        // نبني الخريطة مرة واحدة: الاختيار يحدث لكل مهمة والبحث الخطي في كل مرة عبث.
        _transports = transports.ToDictionary(x => x.ConnectionType);
        _logger = logger;
    }

    /// <summary>يوجّه الإرسال للـ transport الصحيح؛ يرمي PrinterException لنوع غير مدعوم.</summary>
    public Task SendAsync(AgentPrinterDto printer, byte[] data, CancellationToken ct)
    {
        // قيمة غير مسجلة قد تصل من قاعدة بيانات أو كاش أقدم من هذه النسخة (SerialCom مثلا).
        // نفشل برسالة مفهومة بدل KeyNotFoundException يسقط المهمة بكود Unknown.
        if (!_transports.TryGetValue(printer.ConnectionType, out var transport))
        {
            throw new PrinterException(
                PrinterException.Codes.PrinterNotConfigured,
                $"نوع اتصال الطابعة غير مدعوم في هذه النسخة: {printer.ConnectionType}");
        }

        _logger.LogDebug(
            "توجيه {ByteCount} بايت عبر {Transport} ({ConnectionType})",
            data.Length, transport.GetType().Name, printer.ConnectionType);

        return transport.SendAsync(printer, data, ct);
    }
}
