using System.Runtime.InteropServices;

namespace SilentPrintAgent.Interop;

/// <summary>
/// معلومات المستند المرسل للطابور. pDataType يجب أن يكون "RAW" دائما
/// حتى يمرر التعريف البايتات كما هي بدل تفسيرها كنص أو EMF.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DocInfo1
{
    [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
    [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
}

/// <summary>
/// استدعاءات winspool.drv اللازمة للطباعة الخام والتعداد.
/// </summary>
/// <remarks>
/// نستخدم DllImport وليس LibraryImport لأن DocInfo1 تحمل strings فتحتاج marshalling
/// يدويا مع الـ source generator، وهو ما يزيد الكود بلا فائدة هنا.
/// SetLastError مفعّل على الكل لأن تصنيف الأخطاء يعتمد على Marshal.GetLastWin32Error.
/// </remarks>
internal static class WinSpool
{
    internal const int PrinterEnumLocal = 0x00000002;
    internal const int PrinterEnumConnections = 0x00000004;

    internal const int ErrorFileNotFound = 2;
    internal const int ErrorAccessDenied = 5;
    internal const int ErrorNotReady = 21;
    internal const int ErrorInsufficientBuffer = 122;
    internal const int ErrorInvalidPrinterName = 1801;
    internal const int ErrorPrinterDeleted = 1905;

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool OpenPrinter(string printerName, out IntPtr hPrinter, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    internal static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int StartDocPrinter(IntPtr hPrinter, int level, ref DocInfo1 docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    internal static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    internal static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    internal static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    internal static extern bool WritePrinter(
        IntPtr hPrinter, byte[] data, int count, out int written);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool EnumPrinters(
        int flags, string? name, int level,
        IntPtr buffer, int bufferSize,
        out int needed, out int returned);
}

/// <summary>
/// PRINTER_INFO_4: أخف مستوى للتعداد، يعيد الاسم دون فتح كل driver.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct PrinterInfo4
{
    [MarshalAs(UnmanagedType.LPWStr)] public string PrinterName;
    [MarshalAs(UnmanagedType.LPWStr)] public string? ServerName;
    public int Attributes;
}
