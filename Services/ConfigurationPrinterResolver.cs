using SilentPrintAgent.Configuration;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;

namespace SilentPrintAgent.Services;

public sealed class ConfigurationPrinterResolver(PrintAgentOptions options) : IPrinterResolver
{
    public Task<PrinterProfile?> ResolveAsync(string printerId, CancellationToken ct) =>
        Task.FromResult(options.Printers.FirstOrDefault(p => string.Equals(p.PrinterId, printerId, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<PrinterProfile>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PrinterProfile>>(options.Printers.Where(p => p.Enabled).ToArray());
}
