using SilentPrintAgent.Models;

namespace SilentPrintAgent.Services.Transports;

/// <summary>
/// عقد موحد لإيصال ESC/POS bytes إلى طابعة، مهما كانت وسيلة الاتصال.
/// </summary>
/// <remarks>
/// كل تنفيذ مسؤول عن ترجمة أخطائه إلى PrinterException بكود من PrinterException.Codes
/// الموجودة، حتى يظل نفس العطل يعطي نفس الكود في شاشة المتابعة أيا كان طريق الإرسال.
/// </remarks>
public interface IPrinterTransport
{
    /// <summary>نوع الاتصال الذي يخدمه هذا التنفيذ؛ يستخدمه الـ resolver في الاختيار.</summary>
    AgentConnectionType ConnectionType { get; }

    /// <summary>يرسل الـ bytes للطابعة؛ يرمي PrinterException بكود واضح عند أي فشل.</summary>
    Task SendAsync(AgentPrinterDto printer, byte[] data, CancellationToken ct);
}
