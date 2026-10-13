using SilentPrintAgent.Models;

namespace SilentPrintAgent.Core.Models;

public enum PrintPayloadKind { StructuredReceipt = 1, Image = 2, RawEscPos = 3 }
public enum PrintJobStatus { Pending = 1, Retrying = 2, Printing = 3, Completed = 4, DeliveryUnknown = 5, Failed = 6, Cancelled = 7 }
public enum DeliveryState { NotStarted = 1, Acknowledged = 2, Unknown = 3 }

public sealed record PrinterProfile(
    string PrinterId, string DisplayName, AgentConnectionType ConnectionType,
    string? WindowsPrinterName, string? Address, int? Port, byte PaperWidthMm,
    string? CodePage, byte DefaultCopies, bool CashDrawerEnabled, bool Enabled,
    string? CashDrawerCommand = null);

public sealed record PrintPayload(
    PrintPayloadKind Kind, string? Json, byte[]? Bytes, string? ContentType,
    byte Copies = 1, bool OpenCashDrawer = false);

public sealed record PrintFailure(string Code, string Message, bool Retryable, bool DeliveryUnknown);
public sealed record PrintSubmission(string JobId, string ClientId, string RequestHash,
    string PrinterId, PrinterProfile PrinterSnapshot, PrintPayload Payload,
    int MaxAttempts = 3);

public sealed record PrintJob(Guid LocalJobId, string JobId, string ClientId, string RequestHash,
    string PrinterId, PrinterProfile PrinterSnapshot, PrintPayload Payload, PrintJobStatus Status,
    int AttemptCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? NextAttemptAt, string? ActiveLeaseId, DateTimeOffset? LeaseExpiresAt,
    DateTimeOffset? CompletedAt, string? ErrorCode, string? ErrorMessage,
    DeliveryState DeliveryState);

public sealed record JobLease(Guid LocalJobId, string LeaseId, DateTimeOffset ExpiresAt);
public sealed record PrintResult(string JobId, PrintJobStatus Status, DeliveryState DeliveryState,
    string? ErrorCode = null, string? ErrorMessage = null);

public sealed class PrintJobConflictException : Exception
{
    public PrintJobConflictException(string jobId) : base($"Job '{jobId}' was submitted with different content.") { }
}
