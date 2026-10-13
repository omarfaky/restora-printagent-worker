using System.Runtime.InteropServices;
using SilentPrintAgent.Interop;
using SilentPrintAgent.Models;

namespace SilentPrintAgent.Services.Transports;

/// <summary>
/// يرسل ESC/POS bytes لطابعة مثبتة بتعريف على Windows (USB عادة) عبر الـ spooler بوضع RAW.
/// </summary>
/// <remarks>
/// وضع RAW يجعل التعريف يمرر البايتات للجهاز بدون تفسير، وهو ما يجعل نفس مخرجات
/// EscPosRenderer تعمل على USB تماما كما تعمل على TCP.
/// ملاحظة مهمة: النجاح هنا يعني أن البايتات دخلت طابور ويندوز، لا أن الورق خرج.
/// </remarks>
public sealed class WindowsSpoolerTransport(ILogger<WindowsSpoolerTransport> logger) : IPrinterTransport
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    public AgentConnectionType ConnectionType => AgentConnectionType.WindowsPrinter;

    public async Task SendAsync(AgentPrinterDto printer, byte[] data, CancellationToken ct)
    {
        var printerName = printer.WindowsPrinterName?.Trim();

        if (string.IsNullOrWhiteSpace(printerName))
            throw new PrinterException(
                PrinterException.Codes.PrinterNotConfigured,
                "اسم الطابعة في ويندوز غير مضبوط");

        // دوال winspool متزامنة وحاجبة؛ تنفيذها مباشرة يجمد thread من الـ pool
        // ويعطل الـ Local API. والمهلة ضرورية لأن driver معلقا قد لا يعود أبدا.
        try
        {
            await Task.Run(() => SendCore(printerName, data), ct).WaitAsync(SendTimeout, ct);
        }
        catch (TimeoutException)
        {
            throw new PrinterException(
                "DeliveryUnknown",
                $"انتهت مهلة الإرسال للطابعة {printerName}") { DeliveryUncertain = true, Phase = TransportSendPhase.Sending };
        }

        logger.LogInformation(
            "أرسلت {ByteCount} بايت إلى الطابعة {PrinterName} عبر الـ spooler",
            data.Length, printerName);
    }

    private static void SendCore(string printerName, byte[] data)
    {
        if (!WinSpool.OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw ToPrinterException(Marshal.GetLastWin32Error(), printerName, "فتح الطابعة");

        var docStarted = false;
        var pageStarted = false;

        try
        {
            var docInfo = new DocInfo1
            {
                DocName = "Silent Print Agent Receipt",
                OutputFile = null,
                DataType = "RAW"
            };

            if (WinSpool.StartDocPrinter(handle, 1, ref docInfo) == 0)
                throw ToPrinterException(Marshal.GetLastWin32Error(), printerName, "بدء المستند");
            docStarted = true;

            if (!WinSpool.StartPagePrinter(handle))
                throw ToPrinterException(Marshal.GetLastWin32Error(), printerName, "بدء الصفحة");
            pageStarted = true;

            if (!WinSpool.WritePrinter(handle, data, data.Length, out var written))
            {
                var failure = ToPrinterException(Marshal.GetLastWin32Error(), printerName, "كتابة البيانات");
                failure.DeliveryUncertain = true;
                failure.Phase = TransportSendPhase.Sending;
                throw failure;
            }

            // الكتابة الجزئية تعني إيصالا مبتورا؛ نجاح الاستدعاء وحده ليس كافيا.
            if (written != data.Length)
                throw new PrinterException(
                    PrinterException.Codes.Unknown,
                    $"كتابة ناقصة للطابعة {printerName}: {written} من {data.Length} بايت") { DeliveryUncertain = true, Phase = TransportSendPhase.Sending };
        }
        finally
        {
            // مستند أو صفحة مفتوحة بلا إغلاق تعلق الطابور، وتسريب الـ handle
            // يمنع أي طباعة لاحقة حتى إعادة تشغيل الخدمة.
            if (pageStarted) WinSpool.EndPagePrinter(handle);
            if (docStarted) WinSpool.EndDocPrinter(handle);
            WinSpool.ClosePrinter(handle);
        }
    }

    /// <summary>
    /// يترجم خطأ Win32 إلى كود من الأكواد الموجودة فقط، حتى يظل نفس العطل
    /// يعطي نفس الكود في شاشة المتابعة أيا كان طريق الإرسال.
    /// </summary>
    internal static PrinterException ToPrinterException(int win32Error, string printerName, string stage)
    {
        var code = win32Error switch
        {
            WinSpool.ErrorInvalidPrinterName => PrinterException.Codes.PrinterNotConfigured,
            WinSpool.ErrorFileNotFound => PrinterException.Codes.PrinterNotConfigured,
            WinSpool.ErrorAccessDenied => PrinterException.Codes.PrinterNotConfigured,
            WinSpool.ErrorNotReady => PrinterException.Codes.PrinterOffline,
            WinSpool.ErrorPrinterDeleted => PrinterException.Codes.PrinterOffline,
            _ => PrinterException.Codes.Unknown
        };

        return new PrinterException(
            code,
            $"فشل {stage} للطابعة {printerName} - خطأ ويندوز {win32Error}")
        { Phase = stage == "كتابة البيانات" ? TransportSendPhase.Sending : TransportSendPhase.NotStarted,
          DeliveryUncertain = stage == "كتابة البيانات" };
    }
}
