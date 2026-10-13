using System.Text.Json;
using SilentPrintAgent.Models;

namespace SilentPrintAgent.Rendering.Document;

/// <summary>
/// Converts a structured receipt payload into a renderable receipt document.
/// </summary>
/// <remarks>
/// منطق المحتوى منقول كما هو من EscPosRenderer: نفس السطور ونفس الترتيب
/// ونفس الشروط. لا يعرف شيئا عن الطابعة ولا عرض الورق، فيبقى قابلا للاختبار وحده.
/// </remarks>
public sealed class ReceiptDocumentBuilder
{
    /// <summary>ارتفاع السطر التقريبي بالنقاط، يستخدم لترجمة Feed إلى مسافة.</summary>
    private static int LineHeightDots => ReceiptLayout.LineHeightDots;

    /// <summary>اسم النظام في تذييل المطبوعات وفي اختبار الطباعة.</summary>
    public static string SystemName => ReceiptLayout.SystemName;

    public ReceiptDocument BuildReceipt(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var document = new ReceiptDocument();
            var currency = Text(root, "currencyCode") ?? "EGP";

            document.Line(
                Text(root, "cafeName") ?? "upstream application",
                TextAlign.Center,
                TextStyle.Bold | TextStyle.DoubleSize);
            document.Space(LineHeightDots / 2);

            WriteCustomLines(document, root, "headerLines");

            WriteOrderBox(document, root);
            WriteInfoBlock(document, root, includeInvoice: true);

            var orderType = root.GetProperty("orderType").GetString();
            document.Divider();
            document.Line(OrderTypeLabel(orderType), TextAlign.Center, TextStyle.Bold);
            document.Divider();

            if (string.Equals(orderType, "EmployeeConsumption", StringComparison.OrdinalIgnoreCase))
            {
                document.Line("*** طلب موظف ***", TextAlign.Center, TextStyle.Bold);

                if (Text(root, "employeeName") is { } employeeName)
                    document.InfoRow("Employee", employeeName, "الموظف");
            }

            // عناوين الأعمدة بلغتين، كل لغة في سطر حتى تبقى مقروءة على 58mm أيضا.
            document.Line("Item", TextAlign.Center, TextStyle.Bold, "Price", "Qty");
            document.Line("الصنف", TextAlign.Center, TextStyle.Bold, "السعر", "الكمية");
            document.Divider();

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var name = item.GetProperty("name").GetString();
                var quantity = item.GetProperty("quantity").GetInt32();
                var lineTotal = item.GetProperty("lineTotal").GetDecimal();
                var isAddonOnly = item.TryGetProperty("isAddonOnly", out var addonOnly)
                    && addonOnly.ValueKind == JsonValueKind.True;

                document.Line(
                    isAddonOnly ? $"إضافة على {name}" : name ?? string.Empty,
                    TextAlign.Center,
                    trailing: lineTotal.ToString("0.00"),
                    leading: quantity.ToString());

                if (Text(item, "variantName") is { } variant)
                    document.Line($"   {variant}");

                // الإضافات تظهر كسطور مستقلة: العميل يجب أن يرى ما دفع فيه.
                foreach (var addon in item.GetProperty("addons").EnumerateArray())
                    document.Line($"   + {addon.GetProperty("name").GetString()} ×{addon.GetProperty("quantityPerItem").GetInt32()}");

                if (Text(item, "note") is { } note)
                    document.Line($"   * {note}");
            }

            WriteExtraLines(document, root);

            document.Divider();

            WriteAmount(document, root, "subtotal", "Subtotal", "المجموع", currency);
            WriteAmount(document, root, "discountAmount", "Discount", "الخصم", currency, skipZero: true, negative: true);
            WriteAmount(document, root, "serviceAmount", "Service", "الخدمة", currency, skipZero: true);
            WriteAmount(document, root, "taxAmount", "Tax", "الضريبة", currency, skipZero: true);

            // الإجمالي بحجم مضاعف: هو الرقم الوحيد الذي يبحث عنه الزبون، وكان
            // بحجم كل سطر آخر فيضيع بين المجموع والخصم والخدمة.
            document.Line(
                "الإجمالي",
                TextAlign.Center,
                TextStyle.Bold | TextStyle.DoubleSize,
                $"{root.GetProperty("totalAmount").GetDecimal():0.00} {currency}",
                "Total");

            document.Divider();

            foreach (var payment in root.GetProperty("payments").EnumerateArray())
                document.Line(
                    payment.GetProperty("methodName").GetString() ?? "دفع",
                    TextAlign.Center,
                    trailing: $"{payment.GetProperty("amount").GetDecimal():0.00} {currency}");

            document.Space(LineHeightDots);

            // الجملة كانت مكتوبة هنا في الكود وانتقلت إلى بيانات ReceiptCustomLine
            // ببذرة في الـ migration: المالك يعدّلها من شاشة الإدارة بلا نشر.
            WriteCustomLines(document, root, "footerLines");

            if (Text(root, "cafePhone") is { } phone)
                document.Line(phone, TextAlign.Center);

            document.Space(LineHeightDots / 2);
            document.Line(
                $"Printed {FormatDate(root.GetProperty("createdAt").GetDateTimeOffset())}",
                TextAlign.Center);
            document.Line($"Powered by {SystemName}", TextAlign.Center);

            return document;
        }
        catch (Exception ex) when (ex is not PrinterException)
        {
            throw new PrinterException(PrinterException.Codes.RenderError,
                $"فشل تجهيز الإيصال: {ex.Message}", ex);
        }
    }

    public ReceiptDocument BuildKitchenTicket(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var document = new ReceiptDocument();

            WriteCustomLines(document, root, "headerLines");

            WriteOrderBox(document, root);
            WriteInfoBlock(document, root, includeInvoice: false);

            var orderType = root.GetProperty("orderType").GetString();
            document.Divider();
            document.Line(OrderTypeLabel(orderType), TextAlign.Center, TextStyle.Bold);
            document.Divider();

            if (string.Equals(orderType, "OwnerConsumption", StringComparison.OrdinalIgnoreCase))
            {
                document.Line("*** طلب مالك ***", TextAlign.Center, TextStyle.Bold | TextStyle.DoubleSize);
                WriteConsumerName(document, root, "المالك");
            }
            else if (string.Equals(orderType, "EmployeeConsumption", StringComparison.OrdinalIgnoreCase))
            {
                WriteConsumerName(document, root, "الموظف");
            }

            document.Line("Item", TextAlign.Center, TextStyle.Bold, leading: "Qty");
            document.Line("الصنف", TextAlign.Center, TextStyle.Bold, leading: "الكمية");
            document.Divider();

            foreach (var item in root.GetProperty("items").EnumerateArray())
            {
                var isAddonOnly = item.TryGetProperty("isAddonOnly", out var addonOnly)
                    && addonOnly.ValueKind == JsonValueKind.True;

                // اسم الصنف بحجم مضاعف: الباريستا يقرأ التذكرة من بعيد ووسط زحمة.
                document.Line(
                    item.GetProperty("name").GetString() ?? string.Empty,
                    TextAlign.Center,
                    TextStyle.Bold | TextStyle.DoubleSize,
                    leading: item.GetProperty("quantity").GetInt32().ToString());

                if (isAddonOnly)
                    document.Line("⚑ إضافة على مشروب جاهز");

                if (Text(item, "variantName") is { } variant)
                    document.Line($"   {variant}");

                foreach (var addon in item.GetProperty("addons").EnumerateArray())
                    document.Line($"   + {addon.GetString()}");

                if (Text(item, "note") is { } note)
                    document.Line($"   * {note}");
            }

            if (Text(root, "customerNote") is { } customerNote)
            {
                document.Divider();
                document.Line($"ملاحظة: {customerNote}");
            }

            WriteCustomLines(document, root, "footerLines");

            document.Divider();
            document.Line("# KITCHEN #", TextAlign.Center, TextStyle.Bold);
            document.Line(
                FormatDate(root.GetProperty("createdAt").GetDateTimeOffset()),
                TextAlign.Center);

            return document;
        }
        catch (Exception ex) when (ex is not PrinterException)
        {
            throw new PrinterException(PrinterException.Codes.RenderError,
                $"فشل تجهيز تذكرة المطبخ: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// يرسم السطور التي كتبها المالك في الموضع المطلوب.
    /// </summary>
    /// <remarks>
    /// الرسم غبي عمدا: اللقطة تصل مفلترة بالتفعيل ومرتبة من السيرفر، فلا شرط
    /// هنا ولا ترتيب. أي قرار يضاف هنا يجب أن يتكرر حرفيا في محرك الـ JS.
    ///
    /// غياب الحقل يعامل كقائمة فارغة لا كخطأ: كافيه بلا جمل مخصصة حالة عادية،
    /// و<c>payload</c> قديم في كاش الجهاز يجب أن يطبع لا أن ينفجر.
    ///
    /// لا فواصل تلقائية حول الكتلة: المالك يضيف سطر فواصل بنفسه إن أراد،
    /// وفرضها يجعل تعطيل كل السطور يترك فاصلين معلقين بلا محتوى بينهما.
    /// </remarks>
    private static void WriteCustomLines(
        ReceiptDocument document,
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var lines)
            || lines.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var line in lines.EnumerateArray())
        {
            var text = Text(line, "text");
            if (text is null)
                continue;

            document.Line(
                text,
                ResolveAlign(line),
                line.TryGetProperty("isBold", out var bold) && bold.ValueKind == JsonValueKind.True
                    ? TextStyle.Bold
                    : TextStyle.None);
        }
    }

    /// <summary>
    /// يحول <c>ReceiptLineAlign</c> القادم رقما إلى محاذاة الرسم.
    /// </summary>
    /// <remarks>
    /// النوعان منفصلان عمدا: الأول عقد بيانات مخزن في قاعدة البيانات، والثاني
    /// تفصيل داخلي في طبقة الرسم. الوسط هو الافتراضي لأنه شكل الجمل المعتاد
    /// على الإيصال، وهو أيضا افتراضي العمود في قاعدة البيانات.
    /// </remarks>
    private static TextAlign ResolveAlign(JsonElement line) =>
        line.TryGetProperty("align", out var align) && align.ValueKind == JsonValueKind.Number
            ? align.GetInt32() switch
            {
                1 => TextAlign.Right,
                3 => TextAlign.Left,
                _ => TextAlign.Center
            }
            : TextAlign.Center;

    /// <summary>
    /// رقم الطلب داخل إطار: هو الرقم الذي ينادى به في الكافيه فيجب أن يقرأ من نظرة.
    /// يفضل رقم الظهور اليومي القصير على رقم المرجع الطويل.
    /// </summary>
    private static void WriteOrderBox(ReceiptDocument document, JsonElement root)
    {
        var reference = root.GetProperty("orderNumber").GetInt32();
        var display = TryDailyTicket(root, out var daily) ? daily : reference;

        document.Box($"طلب رقم  {display}");
    }

    private static void WriteInfoBlock(ReceiptDocument document, JsonElement root, bool includeInvoice)
    {
        // رقم المرجع يظهر كـ Invoice فقط حين يكون رقم الظهور هو المعروض في الإطار،
        // وإلا كررنا نفس الرقم مرتين بلا فائدة.
        if (includeInvoice && TryDailyTicket(root, out _))
            document.InfoRow("Invoice", root.GetProperty("orderNumber").GetInt32().ToString(), "الفاتورة");

        if (Text(root, "cafeName") is { } cafeName)
            document.InfoRow("POS", cafeName, "نقطة البيع");

        if (Text(root, "cashierName") is { } cashierName)
            document.InfoRow("Cashier", cashierName, "الكاشير");

        document.InfoRow(
            "Date",
            FormatDate(root.GetProperty("createdAt").GetDateTimeOffset()),
            "التاريخ");

        if (Text(root, "businessDate") is { } businessDate)
            document.InfoRow("Business day", businessDate, "يوم العمل");

        if (Text(root, "tableName") is { } tableName)
            document.InfoRow("Table", tableName, "الطاولة");
    }

    private static void WriteAmount(
        ReceiptDocument document,
        JsonElement root,
        string property,
        string labelEn,
        string labelAr,
        string currency,
        bool skipZero = false,
        bool negative = false)
    {
        if (!root.TryGetProperty(property, out var element))
            return;

        var amount = element.GetDecimal();
        if (skipZero && amount == 0)
            return;

        // الخصم بإشارته: رقم مجرّد بجوار «الخصم» يُقرأ مضافا لا مخصوما، وصفحة
        // الويب تكتبه بالسالب أصلا — فاختلافهما يجعل نفس الفاتورة ورقتين.
        var sign = negative ? "-" : string.Empty;

        document.Line(labelAr, TextAlign.Center, trailing: $"{sign}{amount:0.00} {currency}", leading: labelEn);
    }

    private static bool TryDailyTicket(JsonElement root, out int value)
    {
        value = 0;
        return root.TryGetProperty("dailyTicketNumber", out var daily)
            && daily.ValueKind == JsonValueKind.Number
            && daily.TryGetInt32(out value);
    }

    /// <summary>يقرأ خاصية نصية غير فارغة، وإلا null - يوحّد فحصا متكررا.</summary>
    /// <summary>
    /// سطور لا يقابلها صنف — رسوم خارجية دخلت الفاتورة بلا سطر أوردر.
    /// </summary>
    /// <remarks>
    /// <b>القراءة بأمان الغياب إلزامية:</b> حمولات مطبوعة قبل هذه الميزة ما زالت في
    /// <c>PrintJob.PayloadJson</c>، وإعادة طباعتها يجب أن تعمل — و<c>GetProperty</c>
    /// عليها يرمي.
    /// <para>
    /// والصياغة مطابقة لسطر الصنف حرفيا: البون يُقرأ كوحدة واحدة، وصيغة مختلفة تجعل
    /// رسم الوقت يبدو ملحقا غريبا. والتفصيل أسطر مزاحة <b>بلا مبلغ</b>، لأن مبلغا لكل
    /// شريحة يجعل مجموع العمود يخالف الإجمالي بقروش التقريب.
    /// </para>
    /// </remarks>
    private static void WriteExtraLines(ReceiptDocument document, JsonElement root)
    {
        if (!root.TryGetProperty("extraLines", out var lines)
            || lines.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var line in lines.EnumerateArray())
        {
            var label = Text(line, "label");
            if (label is null)
                continue;

            var amount = line.TryGetProperty("amount", out var amountElement)
                && amountElement.ValueKind == JsonValueKind.Number
                    ? amountElement.GetDecimal()
                    : 0m;

            document.Line(
                label,
                TextAlign.Center,
                trailing: amount.ToString("0.00"));

            if (Text(line, "detail") is not { } detail)
                continue;

            var separator = (char)10;
            foreach (var part in detail.Split(separator, StringSplitOptions.RemoveEmptyEntries))
                document.Line($"   {part.Trim()}");
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    private static string OrderTypeLabel(string? orderType) => orderType switch
    {
        "DineIn" => "داخل المحل - Dine in",
        "Takeaway" => "تيك أواي - Take away",
        "EmployeeConsumption" => "استهلاك موظف - Employee",
        "OwnerConsumption" => "استهلاك مالك - Owner",
        "Remake" => "إعادة تحضير - Remake",
        _ => orderType ?? string.Empty
    };

    private static void WriteConsumerName(ReceiptDocument document, JsonElement root, string label)
    {
        var name = Text(root, "consumerName") ?? Text(root, "employeeName");

        if (name is not null)
            document.Line($"{label}: {name}");
    }
}
