using System.Collections.Concurrent;

namespace SilentPrintAgent.Services;

/// <summary>
/// dedupe داخل الـ process: مهمة جاري تنفيذها لا تنفذ مرة ثانية مهما كان مصدر الطلب،
/// سواء تسابق الـ Worker مع الـ Local API أو أعاد المتصفح إرسال نفس الأمر.
/// </summary>
public sealed class InFlightJobTracker
{
    private readonly ConcurrentDictionary<int, byte> _inFlight = new();

    /// <summary>يحاول بدء تنفيذ مهمة؛ false يعني أنها جارية بالفعل.</summary>
    public bool TryBegin(int jobId) => _inFlight.TryAdd(jobId, 0);

    /// <summary>ينهي تتبع مهمة بعد اكتمالها أو فشلها.</summary>
    public void End(int jobId) => _inFlight.TryRemove(jobId, out _);
}
