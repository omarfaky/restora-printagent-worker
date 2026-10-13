using SilentPrintAgent.Models;
using SilentPrintAgent.Services.Transports;

namespace SilentPrintAgent.Services;

/// <summary>
/// يفتح درج الكاشير بإرسال أمر ESC/POS للطابعة المتوصل بها الدرج (RJ11).
/// </summary>
public sealed class CashDrawerService(PrinterTransportResolver transports)
{
    /// <summary>أمر فتح الدرج الافتراضي: ESC p 0 25 250.</summary>
    private static readonly byte[] DefaultCommand = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    /// <summary>يرسل أمر فتح الدرج المضبوط في إعدادات الطابعة أو الافتراضي.</summary>
    public Task OpenAsync(AgentPrinterDto printer, CancellationToken ct) =>
        transports.SendAsync(printer, ParseCommand(printer.CashDrawerCommand), ct);

    /// <summary>
    /// يحول سلسلة hex مثل "1B 70 00 19 FA" إلى bytes؛ يرجع الافتراضي لو فارغة أو غير صالحة.
    /// </summary>
    internal static byte[] ParseCommand(string? hexCommand)
    {
        if (string.IsNullOrWhiteSpace(hexCommand))
            return DefaultCommand;

        try
        {
            var parts = hexCommand.Split(
                [' ', '-', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var bytes = parts
                .Select(part => Convert.ToByte(part, 16))
                .ToArray();

            return bytes.Length > 0 ? bytes : DefaultCommand;
        }
        catch (Exception)
        {
            return DefaultCommand;
        }
    }
}
