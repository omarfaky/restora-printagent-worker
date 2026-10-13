using System.Reflection;
using System.Text.Json;

namespace SilentPrintAgent.Rendering;

/// <summary>
/// ثوابت تخطيط الإيصال، مصدرها الوحيد ملف <c>receipt-layout.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// تعريف تخطيط الإيصال المستخدم بواسطة محرك الطباعة ويقرأه
/// محرك الرسم في المتصفح. المحركان يرسمان نفس الإيصال على جهازين مختلفين، وأي
/// رقم يُكتب مرتين ينحرف عند أول تعديل — فيخرج إيصال التابلت مختلفا عن إيصال
/// الويندوز بفروق صغيرة يصعب تفسيرها.
/// </para>
/// <para>
/// تطابق النسختين يفحصه <c>ReceiptLayoutTests</c>؛ بدونه النسخ اليدوي وعد.
/// </para>
/// <para>
/// القيم منقولة حرفيا من الثوابت التي كانت مبعثرة في <c>ReceiptRasterizer</c>
/// و<c>ReceiptDocumentBuilder</c> و<c>EscPosRasterEncoder</c>: الهدف توحيد
/// المصدر لا تغيير الشكل، فأي تعديل هنا يغيّر كل إيصال يُطبع.
/// </para>
/// </remarks>
public static class ReceiptLayout
{
    private const string ResourceName = "SilentPrintAgent.Resources.receipt-layout.json";

    private static readonly Lazy<LayoutValues> Loaded = new(Load, isThreadSafe: true);

    private static LayoutValues Values => Loaded.Value;

    /// <summary>هامش جانبي بالنقاط على كل من الطرفين.</summary>
    public static int MarginDots => Values.MarginDots;

    /// <summary>فراغ أبيض أعلى الإيصال، بالنقاط.</summary>
    public static int PaperTopDots => Values.PaperTopDots;

    /// <summary>
    /// فراغ أبيض أسفل الإيصال، بالنقاط.
    /// </summary>
    /// <remarks>
    /// الهامش داخل الصورة لا في أمر القص عمدا: أمر <c>GS V 65</c> يترك حساب
    /// مسافة السكينة للطابعة، لكن الطابعات الرخيصة لا تدعمه جميعا، وما لا
    /// يدعمه يعود إلى القطع عند الموضع الحالي فيبتلع آخر الإيصال.
    ///
    /// الفراغ المرسوم في الصورة نفسها يعمل مع أي طابعة مهما فهمت من الأوامر،
    /// فهو خط الدفاع الأخير لا الأول. ثبتت الحاجة إليه تجريبيا: طابعتان
    /// مختلفتان أعطتا فقدا مختلفا لنفس البايتات.
    /// </remarks>
    public static int PaperBottomDots => Values.PaperBottomDots;

    /// <summary>حجم الخط الأساسي بالنقاط.</summary>
    public static float BaseTextSize => Values.BaseTextSize;

    /// <summary>معامل تباعد الأسطر مضروبا في حجم الخط.</summary>
    public static float LineSpacing => Values.LineSpacing;

    /// <summary>الفراغ بين أعمدة السطر الثلاثة.</summary>
    public static float ColumnGapDots => Values.ColumnGapDots;

    /// <summary>حشو داخلي حول نص الإطار.</summary>
    public static float BoxPaddingDots => Values.BoxPaddingDots;

    /// <summary>ارتفاع السطر المرجعي المستخدم في المسافات الرأسية.</summary>
    public static int LineHeightDots => Values.LineHeightDots;

    /// <summary>عتبة السواد: النقطة سوداء لو متوسط السطوع أقل منها.</summary>
    public static int DarkThreshold => Values.DarkThreshold;

    /// <summary>أقصى عدد صفوف في أمر GS v 0 الواحد.</summary>
    public static int MaxBandHeight => Values.MaxBandHeight;

    /// <summary>الهامش السفلي بالنقاط الذي يضاف فوق موضع القص.</summary>
    public static byte CutFeedDots => Values.CutFeedDots;

    /// <summary>اسم النظام المطبوع أسفل الإيصال.</summary>
    public static string SystemName => Values.SystemName;

    /// <summary>
    /// عرض الطباعة بالنقاط لمقاس الورق. كل القيم تقبل القسمة على 8 كما يشترط GS v 0.
    /// </summary>
    /// <remarks>
    /// مقاس غير معروف من بيانات قديمة لا يبرر رفض الطباعة، فيسقط على 80mm.
    /// </remarks>
    public static int WidthForPaper(byte paperWidthMm) =>
        Values.PaperWidths.TryGetValue(paperWidthMm.ToString(), out var width)
            ? width
            : Values.PaperWidths["80"];

    private static LayoutValues Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"ملف تخطيط الإيصال غير مدمج في الـ assembly: {ResourceName}");

        var values = JsonSerializer.Deserialize<LayoutValues>(stream, JsonOptions)
            ?? throw new InvalidOperationException("تعذر قراءة ملف تخطيط الإيصال.");

        if (!values.PaperWidths.ContainsKey("80"))
            throw new InvalidOperationException("ملف تخطيط الإيصال بلا عرض افتراضي 80mm.");

        return values;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record LayoutValues(
        int Version,
        int MarginDots,
        int PaperTopDots,
        int PaperBottomDots,
        float BaseTextSize,
        float LineSpacing,
        float ColumnGapDots,
        float BoxPaddingDots,
        int LineHeightDots,
        int DarkThreshold,
        int MaxBandHeight,
        byte CutFeedDots,
        IReadOnlyDictionary<string, int> PaperWidths,
        string FontFamily,
        string SystemName);
}
