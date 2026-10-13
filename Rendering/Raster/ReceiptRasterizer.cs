using SilentPrintAgent.Rendering;
using SilentPrintAgent.Rendering.Document;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace SilentPrintAgent.Rendering.Raster;

/// <summary>
/// يرسم <see cref="ReceiptDocument"/> على صورة أحادية اللون بعرض الورق.
/// </summary>
/// <remarks>
/// الرسم يمر دائما عبر DrawShapedText من HarfBuzz: هو مصدر تشكيل الحروف
/// العربية واتجاهها. DrawText العادي يترجم بنجاح ويطبع حروفا مقطعة ومقلوبة.
/// </remarks>
public sealed class ReceiptRasterizer
{
    // المصدر الوحيد للأرقام هو receipt-layout.json، ونسخته في wwwroot يقرأها
    // محرك الرسم في المتصفح. أي رقم يُكتب هنا مباشرة ينحرف عن محرك التابلت.
    private static int MarginDots => ReceiptLayout.MarginDots;
    private static float BaseTextSize => ReceiptLayout.BaseTextSize;
    private static float LineSpacing => ReceiptLayout.LineSpacing;
    private static float ColumnGapDots => ReceiptLayout.ColumnGapDots;
    private static float BoxPaddingDots => ReceiptLayout.BoxPaddingDots;

    // فراغ أبيض أعلى الإيصال وأسفله: خط الدفاع الأخير ضد اختلاف مواضع السكاكين
    // بين الطابعات، ويعمل مهما كان أمر القص الذي تفهمه الطابعة.
    private static int PaperTopDots => ReceiptLayout.PaperTopDots;
    private static int PaperBottomDots => ReceiptLayout.PaperBottomDots;

    /// <summary>عرض الطباعة بالنقاط لكل مقاس ورق. كلها تقبل القسمة على 8.</summary>
    internal static int WidthForPaper(byte paperWidthMm) =>
        ReceiptLayout.WidthForPaper(paperWidthMm);

    public SKBitmap Render(ReceiptDocument document, int widthDots)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (widthDots % 8 != 0)
            throw new ArgumentException(
                $"عرض الورق يجب أن يقبل القسمة على 8، والقيمة {widthDots}.",
                nameof(widthDots));

        // مرور أول للقياس: الارتفاع غير معروف قبل لف السطور الطويلة،
        // والرسم على صورة أكبر ثم القص يهدر ذاكرة بلا داع.
        var layout = Measure(document, widthDots);

        var bitmap = new SKBitmap(widthDots, Math.Max(layout.Height, 1));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        Draw(canvas, layout, widthDots);

        return bitmap;
    }

    // ───────────────────────── القياس ─────────────────────────

    private sealed record PlacedLine(
        string Text,
        TextAlign Align,
        TextStyle Style,
        string? Trailing,
        string? Leading,
        float Y);

    /// <summary>إطار مرسوم حول نص، بإحداثيات محسوبة في مرور القياس.</summary>
    private sealed record PlacedBox(string Text, TextStyle Style, float Top, float Bottom, float BaselineY);

    private sealed record Layout(
        List<PlacedLine> Lines,
        List<float> Dividers,
        List<PlacedBox> Boxes,
        int Height);

    private static Layout Measure(ReceiptDocument document, int widthDots)
    {
        var lines = new List<PlacedLine>();
        var dividers = new List<float>();
        var boxes = new List<PlacedBox>();
        var usableWidth = widthDots - (MarginDots * 2);
        var y = (float)PaperTopDots;

        foreach (var element in document.Elements)
        {
            switch (element)
            {
                case ReceiptSpace space:
                    y += space.Dots;
                    break;

                case ReceiptDivider:
                    y += BaseTextSize * 0.5f;
                    dividers.Add(y);
                    y += BaseTextSize * 0.5f;
                    break;

                case ReceiptBox box:
                    using (var boxFont = CreateFont(box.Style))
                    {
                        var top = y;
                        var baseline = y + BoxPaddingDots + boxFont.Size;
                        var bottom = baseline + BoxPaddingDots;

                        boxes.Add(new PlacedBox(box.Text, box.Style, top, bottom, baseline));
                        y = bottom + (BaseTextSize * 0.4f);
                    }

                    break;

                case ReceiptLine line:
                    var font = CreateFont(line.Style);
                    var lineHeight = font.Size * LineSpacing;

                    // الأعمدة الجانبية تحجز مكانها فلا يتداخل معها النص الأوسط،
                    // وتبقى مع السطر الأول فقط لأن الالتفاف يخص النص وحده.
                    var reserved =
                        (line.Trailing is null ? 0f : MeasureWidth(line.Trailing, font) + ColumnGapDots) +
                        (line.Leading is null ? 0f : MeasureWidth(line.Leading, font) + ColumnGapDots);

                    var segments = Wrap(line.Text, font, usableWidth - reserved, usableWidth - reserved);
                    foreach (var (segment, index) in segments.Select((s, i) => (s, i)))
                    {
                        lines.Add(new PlacedLine(
                            segment,
                            line.Align,
                            line.Style,
                            index == 0 ? line.Trailing : null,
                            index == 0 ? line.Leading : null,
                            y + font.Size));

                        y += lineHeight;
                    }

                    break;
            }
        }

        return new Layout(lines, dividers, boxes, (int)Math.Ceiling(y) + PaperBottomDots);
    }

    /// <summary>
    /// يلف النص بدل قصه: اسم صنف طويل يُقص بصمت يعني معلومة ضائعة على الورق.
    /// </summary>
    private static List<string> Wrap(string text, SKFont font, float firstWidth, float restWidth)
    {
        if (string.IsNullOrEmpty(text))
            return [string.Empty];

        var available = Math.Max(firstWidth, 40f);
        if (MeasureWidth(text, font) <= available)
            return [text];

        var result = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";

            if (MeasureWidth(candidate, font) <= available)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
                result.Add(current);

            current = word;
            available = restWidth;
        }

        if (current.Length > 0)
            result.Add(current);

        return result.Count > 0 ? result : [text];
    }

    private static float MeasureWidth(string text, SKFont font)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        using var shaper = new SKShaper(font.Typeface);

        // القياس يجمع عرض المقاطع لأن الرسم يتم مقطعا مقطعا؛
        // قياس النص كاملا مرة واحدة يعطي رقما لا يطابق ما يُرسم فعلا.
        return BidiText.Split(text).Sum(run => shaper.Shape(run.Text, font).Width);
    }

    // ───────────────────────── الرسم ─────────────────────────

    private static void Draw(SKCanvas canvas, Layout layout, int widthDots)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            // بلا تنعيم: الصورة تتحول لبت واحد لاحقا، والرمادي يصير نقاطا عشوائية.
            IsAntialias = false
        };

        foreach (var divider in layout.Dividers)
            canvas.DrawRect(MarginDots, divider, widthDots - (MarginDots * 2), 1.5f, paint);

        foreach (var box in layout.Boxes)
            DrawBox(canvas, box, paint, widthDots);

        foreach (var line in layout.Lines)
        {
            using var font = CreateFont(line.Style);

            if (!string.IsNullOrEmpty(line.Text))
                DrawAligned(canvas, line.Text, line.Align, line.Y, font, paint, widthDots);

            // الأعمدة الجانبية تحاذى لطرفيها بقياس عرضها الفعلي لا بعدد محارفها.
            if (!string.IsNullOrEmpty(line.Trailing))
                DrawAligned(canvas, line.Trailing, TextAlign.Right, line.Y, font, paint, widthDots);

            if (!string.IsNullOrEmpty(line.Leading))
                DrawAligned(canvas, line.Leading, TextAlign.Left, line.Y, font, paint, widthDots);
        }
    }

    /// <summary>
    /// إطار مزدوج حول رقم الطلب: الرقم الذي ينادى به في الكافيه يجب أن يقرأ من نظرة.
    /// </summary>
    private static void DrawBox(SKCanvas canvas, PlacedBox box, SKPaint paint, int widthDots)
    {
        using var font = CreateFont(box.Style);
        var width = MeasureWidth(box.Text, font);

        var boxWidth = Math.Min(width + (BoxPaddingDots * 6), widthDots - (MarginDots * 2f));
        var left = (widthDots - boxWidth) / 2f;
        var height = box.Bottom - box.Top;

        using var frame = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };

        canvas.DrawRect(left, box.Top, boxWidth, height, frame);
        canvas.DrawRect(left + 4f, box.Top + 4f, boxWidth - 8f, height - 8f, frame);

        var x = left + ((boxWidth - width) / 2f);

        using var shaper = new SKShaper(font.Typeface);
        foreach (var run in BidiText.Split(box.Text))
        {
            canvas.DrawShapedText(shaper, run.Text, x, box.BaselineY, font, paint);
            x += shaper.Shape(run.Text, font).Width;
        }
    }

    private static void DrawAligned(
        SKCanvas canvas,
        string text,
        TextAlign align,
        float y,
        SKFont font,
        SKPaint paint,
        int widthDots)
    {
        var width = MeasureWidth(text, font);

        var x = align switch
        {
            TextAlign.Left => MarginDots,
            TextAlign.Center => (widthDots - width) / 2f,
            _ => widthDots - MarginDots - width
        };

        // الرسم مقطعا مقطعا بالترتيب البصري: المقاطع تأتي مرتبة يسارا←يمينا
        // من BidiText، وكل مقطع متجانس الاتجاه فيشكّله HarfBuzz صحيحا وحده.
        using var shaper = new SKShaper(font.Typeface);

        foreach (var run in BidiText.Split(text))
        {
            canvas.DrawShapedText(shaper, run.Text, x, y, font, paint);
            x += shaper.Shape(run.Text, font).Width;
        }
    }

    private static SKFont CreateFont(TextStyle style)
    {
        var size = style.HasFlag(TextStyle.DoubleSize) ? BaseTextSize * 1.6f : BaseTextSize;

        return new SKFont(ReceiptFont.Typeface, size)
        {
            // العريض يُحاكى بزيادة سمك الحواف لأن الخط المدمج وزن واحد.
            Embolden = style.HasFlag(TextStyle.Bold),
            Edging = SKFontEdging.Alias
        };
    }
}
