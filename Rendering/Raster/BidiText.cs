namespace SilentPrintAgent.Rendering.Raster;

/// <summary>اتجاه مقطع نصي.</summary>
internal enum RunDirection
{
    LeftToRight,
    RightToLeft
}

/// <summary>مقطع متجانس الاتجاه، جاهز للتشكيل والرسم وحده.</summary>
internal sealed record TextRun(string Text, RunDirection Direction);

/// <summary>
/// تقسيم السطر إلى مقاطع حسب الاتجاه وترتيبها بصريا.
/// </summary>
/// <remarks>
/// HarfBuzz يشكّل المقطع الواحد ويرتبه داخليا، لكنه لا يطبق خوارزمية
/// الاتجاه ثنائي الاتجاه (UAX#9) على السطر المختلط. بدون هذه الطبقة تخرج
/// الأرقام معكوسة (213 تصير 312) وترتيب الكلمات العربية مقلوبا في أي سطر
/// يخلط عربي مع أرقام أو إنجليزي — وهو أغلب سطور الإيصال.
///
/// هذه نسخة مبسطة تكفي محتوى الإيصالات: أسماء أصناف وأرقام ومبالغ وتواريخ.
/// ليست تنفيذا كاملا لـ UAX#9 ولا تدعم التداخل العميق للاتجاهات.
/// </remarks>
internal static class BidiText
{
    private enum CharClass
    {
        Rtl,
        Ltr,
        Number,
        Neutral
    }

    /// <summary>يقسم النص إلى مقاطع مرتبة بصريا من اليسار إلى اليمين.</summary>
    public static IReadOnlyList<TextRun> Split(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [new TextRun(text, RunDirection.LeftToRight)];

        var classes = text.Select(Classify).ToArray();
        var baseDirection = ResolveBaseDirection(classes);
        var resolved = ResolveNeutrals(classes, baseDirection);
        var runs = GroupRuns(text, resolved);

        // في السطر ذي الأساس RTL يوضع أول مقطع منطقيا في أقصى اليمين،
        // فالترتيب البصري (يسار←يمين) هو عكس الترتيب المنطقي.
        if (baseDirection == RunDirection.RightToLeft)
            runs.Reverse();

        return runs;
    }

    /// <summary>اتجاه السطر من أول حرف قوي فيه؛ السطر بلا حروف قوية يبقى LTR.</summary>
    private static RunDirection ResolveBaseDirection(CharClass[] classes)
    {
        foreach (var characterClass in classes)
        {
            if (characterClass == CharClass.Rtl)
                return RunDirection.RightToLeft;

            if (characterClass == CharClass.Ltr)
                return RunDirection.LeftToRight;
        }

        // تاريخ أو مبلغ وحده يبقى كما هو: 2026-08-15 لا 51-80-6202.
        return RunDirection.LeftToRight;
    }

    /// <summary>
    /// المحايدات (مسافات وعلامات) تأخذ اتجاه ما حولها لو اتفق الجانبان،
    /// وإلا تأخذ اتجاه السطر. الأرقام تبقى LTR دائما فتظهر بترتيبها الصحيح.
    /// </summary>
    private static RunDirection[] ResolveNeutrals(CharClass[] classes, RunDirection baseDirection)
    {
        var resolved = new RunDirection[classes.Length];

        for (var i = 0; i < classes.Length; i++)
        {
            resolved[i] = classes[i] switch
            {
                CharClass.Rtl => RunDirection.RightToLeft,
                CharClass.Ltr => RunDirection.LeftToRight,
                CharClass.Number => RunDirection.LeftToRight,
                _ => Surrounding(classes, i, baseDirection)
            };
        }

        return resolved;
    }

    private static RunDirection Surrounding(CharClass[] classes, int index, RunDirection baseDirection)
    {
        var before = StrongBefore(classes, index);
        var after = StrongAfter(classes, index);

        if (before is not null && before == after)
            return before.Value;

        return baseDirection;
    }

    private static RunDirection? StrongBefore(CharClass[] classes, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (classes[i] is CharClass.Rtl)
                return RunDirection.RightToLeft;

            if (classes[i] is CharClass.Ltr or CharClass.Number)
                return RunDirection.LeftToRight;
        }

        return null;
    }

    private static RunDirection? StrongAfter(CharClass[] classes, int index)
    {
        for (var i = index + 1; i < classes.Length; i++)
        {
            if (classes[i] is CharClass.Rtl)
                return RunDirection.RightToLeft;

            if (classes[i] is CharClass.Ltr or CharClass.Number)
                return RunDirection.LeftToRight;
        }

        return null;
    }

    private static List<TextRun> GroupRuns(string text, RunDirection[] resolved)
    {
        var runs = new List<TextRun>();
        var start = 0;

        for (var i = 1; i <= resolved.Length; i++)
        {
            if (i < resolved.Length && resolved[i] == resolved[start])
                continue;

            runs.Add(new TextRun(text[start..i], resolved[start]));
            start = i;
        }

        return runs;
    }

    private static CharClass Classify(char value)
    {
        if (char.IsDigit(value))
            return CharClass.Number;

        if (IsArabic(value))
            return CharClass.Rtl;

        if (char.IsLetter(value))
            return CharClass.Ltr;

        return CharClass.Neutral;
    }

    /// <summary>النطاقات العربية الأساسية مع أشكال العرض والملحقات.</summary>
    private static bool IsArabic(char value) =>
        value is >= '؀' and <= 'ۿ'    // Arabic
            or >= 'ݐ' and <= 'ݿ'      // Arabic Supplement
            or >= 'ࢠ' and <= 'ࣿ'      // Arabic Extended-A
            or >= 'ﭐ' and <= '﷿'      // Arabic Presentation Forms-A
            or >= 'ﹰ' and <= '﻿';     // Arabic Presentation Forms-B
}
