using System.Runtime.InteropServices;
using SilentPrintAgent.Interop;

namespace SilentPrintAgent.Services;

/// <summary>
/// يعدّد الطابعات المثبتة عبر EnumPrinters.
/// </summary>
/// <remarks>
/// الاكتشاف وسيلة راحة للواجهة فقط؛ أي فشل يعيد قائمة فارغة ولا يرمي،
/// حتى يظل الإدخال اليدوي ممكنا لو تعذر التعداد.
/// </remarks>
public sealed class InstalledPrinterProvider(ILogger<InstalledPrinterProvider> logger)
    : IInstalledPrinterProvider
{
    // LOCAL وحدها تخفي الطابعات المشاركة على الشبكة التي قد يستخدمها المحل فعلا.
    private const int Flags = WinSpool.PrinterEnumLocal | WinSpool.PrinterEnumConnections;

    // PRINTER_INFO_4: أخف مستوى، يعيد الاسم دون فتح كل driver.
    private const int Level = 4;

    public IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            return Enumerate();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "تعذر تعداد الطابعات المثبتة.");
            return [];
        }
    }

    private static List<string> Enumerate()
    {
        // النداء الأول بحجم صفر لمعرفة الحجم المطلوب؛ فشله بـ ERROR_INSUFFICIENT_BUFFER متوقع.
        WinSpool.EnumPrinters(Flags, null, Level, IntPtr.Zero, 0, out var needed, out _);

        if (needed <= 0)
            return [];

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!WinSpool.EnumPrinters(Flags, null, Level, buffer, needed, out _, out var returned))
                return [];

            var size = Marshal.SizeOf<PrinterInfo4>();
            var names = new List<string>(returned);

            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<PrinterInfo4>(buffer + (i * size));
                if (!string.IsNullOrWhiteSpace(info.PrinterName))
                    names.Add(info.PrinterName);
            }

            // الترتيب الثابت بين الفتحات يقلل أخطاء الاختيار في الواجهة.
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
