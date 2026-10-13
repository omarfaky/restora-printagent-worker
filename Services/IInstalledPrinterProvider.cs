namespace SilentPrintAgent.Services;

/// <summary>
/// يعدّد الطابعات المثبتة على الجهاز حتى تختار شاشة الإعدادات من قائمة
/// بدل كتابة اسم الطابعة يدويا.
/// </summary>
public interface IInstalledPrinterProvider
{
    /// <summary>أسماء الطابعات كما تظهر في Printers &amp; Scanners، مرتبة أبجديا.</summary>
    IReadOnlyList<string> GetInstalledPrinters();
}
