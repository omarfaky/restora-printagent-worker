namespace SilentPrintAgent.Rendering.Document;

/// <summary>محاذاة السطر داخل عرض الورق.</summary>
public enum TextAlign
{
    Right,
    Center,
    Left
}

/// <summary>نمط السطر. Flags لأن السطر قد يكون عريضا ومكبرا معا.</summary>
[Flags]
public enum TextStyle
{
    None = 0,
    Bold = 1,
    DoubleSize = 2
}

/// <summary>عنصر واحد داخل الإيصال.</summary>
public interface IReceiptElement;

/// <summary>
/// سطر نصي بثلاثة مواضع اختيارية على نفس الارتفاع.
/// </summary>
/// <remarks>
/// <paramref name="Leading"/> يلتصق بالطرف الأيسر و<paramref name="Trailing"/> بالأيمن،
/// و<paramref name="Text"/> يشغل ما بينهما ويلتف داخله. الأعمدة تقاس بعرضها الفعلي
/// وقت الرسم لا بعدد محارفها، لأن عد المحارف يغلط مع العربي.
///
/// هذا الشكل يخدم صفوف البيانات (Invoice | 12907774 | الفاتورة)
/// وصفوف الأصناف (الكمية | الاسم | السعر) وصفوف المجاميع بنفس البنية.
/// </remarks>
public sealed record ReceiptLine(
    string Text,
    TextAlign Align = TextAlign.Right,
    TextStyle Style = TextStyle.None,
    string? Trailing = null,
    string? Leading = null) : IReceiptElement;

/// <summary>نص داخل إطار مزدوج، لرقم الطلب الذي ينادى به في الكافيه.</summary>
public sealed record ReceiptBox(
    string Text,
    TextStyle Style = TextStyle.Bold | TextStyle.DoubleSize) : IReceiptElement;

/// <summary>فاصل أفقي يرسم بعرض الورق الفعلي، لا بتكرار محرف.</summary>
public sealed record ReceiptDivider : IReceiptElement;

/// <summary>مسافة رأسية بالنقاط.</summary>
public sealed record ReceiptSpace(int Dots) : IReceiptElement;

/// <summary>
/// تمثيل محايد للإيصال: سطور بمحاذاة وأنماط، بلا أي معرفة بـ ESC/POS أو الرسم.
/// </summary>
/// <remarks>
/// هذا الفصل هو ما يجعل محتوى الإيصال قابلا للاختبار بدون طابعة ولا خطوط:
/// المستند يوصف نصا، والرسم طبقة خلفه لا تغير المحتوى.
/// </remarks>
public sealed class ReceiptDocument
{
    private readonly List<IReceiptElement> _elements = [];

    public IReadOnlyList<IReceiptElement> Elements => _elements;

    public void Add(IReceiptElement element) => _elements.Add(element);

    public void Line(
        string text,
        TextAlign align = TextAlign.Right,
        TextStyle style = TextStyle.None,
        string? trailing = null,
        string? leading = null) =>
        Add(new ReceiptLine(text, align, style, trailing, leading));

    /// <summary>صف ثنائي اللغة: تسمية إنجليزية يسارا، قيمة في الوسط، تسمية عربية يمينا.</summary>
    public void InfoRow(string labelEn, string value, string labelAr) =>
        Add(new ReceiptLine(value, TextAlign.Center, TextStyle.None, labelAr, labelEn));

    public void Box(string text) => Add(new ReceiptBox(text));

    public void Divider() => Add(new ReceiptDivider());

    public void Space(int dots) => Add(new ReceiptSpace(dots));
}
