using SilentPrintAgent.Core.Models;

namespace SilentPrintAgent.Configuration;

public sealed class PrintAgentOptions
{
    public const string SectionName = "PrintAgent";
    public string DataDirectory { get; set; } = string.Empty;
    public int LocalApiPort { get; set; } = 5055;
    public string LocalToken { get; set; } = string.Empty;
    public string ClientId { get; set; } = "default";
    public string[] AllowedOrigins { get; set; } = [];
    public bool AllowRawEscPos { get; set; }
    public int MaxReceiptJsonBytes { get; set; } = 262144;
    public int MaxImageBytes { get; set; } = 8388608;
    public int MaxRawEscPosBytes { get; set; } = 1048576;
    public int MaxImageWidthPx { get; set; } = 2048;
    public int MaxImageHeightPx { get; set; } = 16000;
    public byte MaxCopies { get; set; } = 10;
    public int MaxAttempts { get; set; } = 3;
    public int LeaseDurationSeconds { get; set; } = 120;
    public int RetryInitialDelaySeconds { get; set; } = 2;
    public int RetryMaxDelaySeconds { get; set; } = 60;
    public int CompletedRetentionDays { get; set; } = 7;
    public int DeliveryUnknownRetentionDays { get; set; } = 30;
    public List<PrinterProfile> Printers { get; set; } = [];

    public string ResolveDataDirectory()
    {
        if (!string.IsNullOrWhiteSpace(DataDirectory)) return Path.GetFullPath(DataDirectory);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(string.IsNullOrWhiteSpace(common) ? AppContext.BaseDirectory : common, "SilentPrintAgent", "data");
    }
}

