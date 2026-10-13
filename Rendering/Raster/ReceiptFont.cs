using System.Reflection;
using SkiaSharp;

namespace SilentPrintAgent.Rendering.Raster;

/// <summary>
/// يحمّل الخط العربي المدمج مرة واحدة لعمر العملية.
/// </summary>
/// <remarks>
/// التحميل من مورد مدمج لا من خطوط النظام: أجهزة الكاشير غير متطابقة،
/// وغياب الخط وقت الطباعة يعني إيصالا فارغا في أسوأ توقيت.
/// </remarks>
internal static class ReceiptFont
{
    private const string ResourceName =
        "SilentPrintAgent.Resources.Fonts.NotoNaskhArabic-Regular.ttf";

    private static readonly Lazy<SKTypeface> Loaded = new(Load, isThreadSafe: true);

    public static SKTypeface Typeface => Loaded.Value;

    private static SKTypeface Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"الخط المدمج غير موجود في الـ assembly: {ResourceName}");

        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException("تعذر قراءة الخط المدمج.");
    }
}
