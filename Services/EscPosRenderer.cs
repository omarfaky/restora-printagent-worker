using System.Text;
using SilentPrintAgent.Models;
using SilentPrintAgent.Rendering;
using SilentPrintAgent.Rendering.Document;
using SilentPrintAgent.Rendering.Raster;

namespace SilentPrintAgent.Services;

/// <summary>
/// Converts a structured receipt payload into ESC/POS bytes.
/// </summary>
/// <remarks>
/// الإيصال وتذكرة المطبخ يخرجان **صورة نقطية** (GS v 0): code pages الطابعة
/// لا تعطي عربيا مقروءا مهما ضُبطت لأنها بلا تشكيل حروف ولا ترتيب اتجاه.
/// اختبار الطباعة وحده يبقى نصيا إنجليزيا حتى يظل أداة تشخيص تعمل
/// ولو انكسر الخط أو الرسم، فتفصل عطل الاتصال عن عطل الرندرة.
/// </remarks>
public sealed class EscPosRenderer
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;

    /// <summary>
    /// هامش الورق أسفل آخر سطر، بالنقاط، يضاف فوق موضع القص الذي تحسبه الطابعة.
    /// </summary>
    /// <remarks>
    /// 24 نقطة ≈ 3 مم على طابعة 203dpi: مسافة كافية ليمسك الكاشير الورقة
    /// دون إهدار ورق في كل إيصال.
    /// </remarks>
    private static byte CutFeedDots => ReceiptLayout.CutFeedDots;

    private readonly ReceiptDocumentBuilder _documentBuilder = new();
    private readonly ReceiptRasterizer _rasterizer = new();
    private readonly EscPosRasterEncoder _encoder = new();

    static EscPosRenderer()
    {
        // يخدم مسار الاختبار النصي فقط؛ الصورة لا تمر بأي code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public byte[] RenderReceipt(string payloadJson, AgentPrinterDto printer) =>
        RenderAsRaster(_documentBuilder.BuildReceipt(payloadJson), printer);

    public byte[] RenderKitchenTicket(string payloadJson, AgentPrinterDto printer) =>
        RenderAsRaster(_documentBuilder.BuildKitchenTicket(payloadJson), printer);

    /// <summary>اختبار اتصال نصي بالإنجليزية؛ لا يعتمد على الخط ولا على الرسم.</summary>
    public byte[] RenderTest(AgentPrinterDto printer)
    {
        var builder = new EscPosBuilder(printer);
        builder.Initialize();
        builder.AlignCenter();
        builder.BoldOn();
        builder.Line(ReceiptDocumentBuilder.SystemName);
        builder.BoldOff();
        builder.Line("Test Print");
        builder.Line(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.Cut();
        return builder.ToBytes();
    }

    private byte[] RenderAsRaster(ReceiptDocument document, AgentPrinterDto printer)
    {
        try
        {
            var width = ReceiptRasterizer.WidthForPaper(printer.PaperWidthMm);

            // SKBitmap يملك ذاكرة أصلية لا يراها الـ GC؛ تسريبها يقتل خدمة تعمل شهورا.
            using var bitmap = _rasterizer.Render(document, width);

            var body = _encoder.Encode(bitmap);
            var output = new List<byte>(body.Length + 16);

            output.AddRange([Esc, 0x40]);                    // ESC @ initialize
            output.AddRange(body);

            // GS V 65 n: تغذية حتى موضع القص زائد n ثم قص كامل.
            //
            // البديل الذي كان مستخدما — ESC d 3 ثم GS V 0 — يقطع عند موضع الورق
            // الحالي، والسكينة تبعد عن رأس الطباعة نحو 15 مم بينما التغذية ثلاثة
            // أسطر (~11 مم)، فكان آخر الإيصال يقع تحت السكينة ويضيع في كل طباعة.
            // ثبت ذلك بالتجربة: نفس الصورة بلا أمر قص تخرج كاملة، ومعه ينقص آخرها.
            //
            // هذا الأمر يزيل التخمين: الطابعة وحدها تعرف مسافة رأسها من سكينتها،
            // وتغذّيها بنفسها، فيبقى n هامشا سفليا لا تعويضا عن عطل.
            output.AddRange([Gs, 0x56, 0x41, CutFeedDots]);

            return [.. output];
        }
        catch (Exception ex) when (ex is not PrinterException)
        {
            throw new PrinterException(PrinterException.Codes.RenderError,
                $"فشل رسم الإيصال: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// يبني تسلسل ESC/POS نصيا. لم يعد يخدم الإيصالات، لكنه يظل أداة
    /// اختبار الاتصال المستقلة عن طبقة الرسم.
    /// </summary>
    private sealed class EscPosBuilder(AgentPrinterDto printer)
    {
        private readonly List<byte> _bytes = [];
        private readonly Encoding _encoding = ResolveEncoding(printer.CodePage);

        public void Initialize() => _bytes.AddRange([Esc, 0x40]);

        public void AlignCenter() => _bytes.AddRange([Esc, 0x61, 0x01]);

        public void BoldOn() => _bytes.AddRange([Esc, 0x45, 0x01]);

        public void BoldOff() => _bytes.AddRange([Esc, 0x45, 0x00]);

        public void Line(string text)
        {
            _bytes.AddRange(_encoding.GetBytes(text));
            _bytes.Add(0x0A);
        }

        /// <summary>يغذّي حتى موضع القص ثم يقص، بنفس أمر مسار الصورة.</summary>
        public void Cut() => _bytes.AddRange([Gs, 0x56, 0x41, CutFeedDots]);

        public byte[] ToBytes() => [.. _bytes];

        private static Encoding ResolveEncoding(string? codePage)
        {
            if (string.IsNullOrWhiteSpace(codePage))
                return Encoding.ASCII;

            try
            {
                return Encoding.GetEncoding(codePage.Trim());
            }
            catch (ArgumentException)
            {
                return Encoding.ASCII;
            }
        }
    }
}
