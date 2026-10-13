using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;

namespace SilentPrintAgent.Services.Rendering;

public static class PrinterProfileExtensions
{
    public static AgentPrinterDto ToLegacy(this PrinterProfile profile, byte? copies = null) => new(
        profile.ConnectionType, profile.Address, profile.Port, profile.PaperWidthMm, profile.CodePage,
        copies ?? profile.DefaultCopies, profile.CashDrawerEnabled, profile.CashDrawerCommand, profile.WindowsPrinterName);
}
