namespace SilentPrintAgent.Models;

/// <summary>
/// Local connection type used by configured printer profiles.
/// </summary>
public enum AgentConnectionType : byte
{
    /// <summary>طابعة شبكة ESC/POS عبر TCP socket، عادة على port 9100.</summary>
    NetworkEscPos = 1,

    /// <summary>طابعة مثبتة بتعريف على Windows (USB أو مشاركة شبكة) عبر الـ spooler بوضع RAW.</summary>
    WindowsPrinter = 2,

    /// <summary>طابعة على منفذ COM. غير منفذة بعد.</summary>
    SerialCom = 3
}

/// <summary>
/// إعدادات الطابعة المستهدفة المرافقة لكل مهمة.
/// </summary>
public sealed record AgentPrinterDto(
    AgentConnectionType ConnectionType,
    string? Address,
    int? Port,
    byte PaperWidthMm,
    string? CodePage,
    byte Copies,
    bool CashDrawerEnabled,
    string? CashDrawerCommand,
    string? WindowsPrinterName = null);

/// <summary>
/// Printer transport failure with a stable machine-readable code.
/// </summary>
public enum TransportSendPhase
{
    NotStarted = 1,
    Connecting = 2,
    Sending = 3,
    SendCompleted = 4,
    AcceptedBySpooler = 5
}

public sealed class PrinterException(string errorCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string ErrorCode { get; } = errorCode;

    /// <summary>Whether transport may have accepted part or all of this command.</summary>
    public bool DeliveryUncertain { get; set; }
    public TransportSendPhase Phase { get; set; } = TransportSendPhase.NotStarted;

    public static class Codes
    {
        public const string PrinterOffline = "PrinterOffline";
        public const string ConnectionTimeout = "ConnectionTimeout";
        public const string RenderError = "RenderError";
        public const string PrinterNotConfigured = "PrinterNotConfigured";
        public const string Unknown = "Unknown";
    }
}
