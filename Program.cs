using System.Net;
using Microsoft.AspNetCore.Http.Features;
using SilentPrintAgent.LocalApi;
using SilentPrintAgent.Configuration;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;
using SilentPrintAgent.Services;
using SilentPrintAgent.Services.Queue;
using SilentPrintAgent.Services.Rendering;
using SilentPrintAgent.Services.Transports;
using SilentPrintAgent.Workers;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

var printOptions = builder.Configuration
    .GetSection(PrintAgentOptions.SectionName)
    .Get<PrintAgentOptions>() ?? new PrintAgentOptions();

var printerSections = builder.Configuration.GetSection($"{PrintAgentOptions.SectionName}:Printers").GetChildren().ToArray();
if (printerSections.Length > 0)
{
    printOptions.Printers = printerSections.Select(section => new PrinterProfile(
        section["PrinterId"] ?? section.Key,
        section["DisplayName"] ?? section["PrinterId"] ?? section.Key,
        Enum.TryParse<AgentConnectionType>(section["ConnectionType"], true, out var connectionType) ? connectionType : AgentConnectionType.NetworkEscPos,
        section["WindowsPrinterName"], section["Address"], int.TryParse(section["Port"], out var port) ? port : null,
        byte.TryParse(section["PaperWidthMm"], out var paperWidth) ? paperWidth : (byte)80,
        section["CodePage"], byte.TryParse(section["DefaultCopies"], out var copies) ? copies : (byte)1,
        bool.TryParse(section["CashDrawerEnabled"], out var drawerEnabled) && drawerEnabled,
        bool.TryParse(section["Enabled"], out var enabled) && enabled,
        section["CashDrawerCommand"])).ToList();
}

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = Math.Max(Math.Max(printOptions.MaxImageBytes, printOptions.MaxReceiptJsonBytes), printOptions.MaxRawEscPosBytes) + 65536L;
    kestrel.Listen(IPAddress.Loopback, printOptions.LocalApiPort);
});
builder.Services.Configure<FormOptions>(form => form.MultipartBodyLengthLimit = Math.Max(printOptions.MaxImageBytes, printOptions.MaxRawEscPosBytes) + 65536L);

builder.Services.AddSingleton(printOptions);
builder.Services.AddSingleton<InFlightJobTracker>();
builder.Services.AddSingleton<EscPosRenderer>();
builder.Services.AddSingleton<IPrinterTransport, TcpPrinterTransport>();
builder.Services.AddSingleton<IPrinterTransport, WindowsSpoolerTransport>();
builder.Services.AddSingleton<PrinterTransportResolver>();
builder.Services.AddSingleton<IInstalledPrinterProvider, InstalledPrinterProvider>();
builder.Services.AddSingleton<CashDrawerService>();
builder.Services.AddSingleton<IPrinterResolver, ConfigurationPrinterResolver>();
builder.Services.AddSingleton<IPayloadRenderer, StructuredReceiptPayloadRenderer>();
builder.Services.AddSingleton<IPayloadRenderer, ImagePayloadRenderer>();
builder.Services.AddSingleton<IPayloadRenderer, RawEscPosPayloadRenderer>();
builder.Services.AddSingleton<IPrintJobExecutor, GenericPrintJobExecutor>();
builder.Services.AddSingleton<IPrintQueue, InProcessPrintQueue>();
builder.Services.AddSingleton<IPrintJobStore>(_ => new SqlitePrintJobStore(Path.Combine(printOptions.ResolveDataDirectory(), "print-agent.db")));
builder.Services.AddHostedService<PrintQueueWorker>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    var limits = context.Request.Path.Value switch
    {
        "/api/v1/print/receipt" => printOptions.MaxReceiptJsonBytes + 4096L,
        "/api/v1/print/image" => printOptions.MaxImageBytes + 65536L,
        "/api/v1/print/raw" => printOptions.MaxRawEscPosBytes + 65536L,
        _ => (long?)null
    };
    var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (limits is not null && feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limits.Value;
    await next();
});
app.UseMiddleware<LocalCorsMiddleware>();
app.MapGet("/health", (PrintAgentOptions options) => Results.Ok(new
{
    ok = true,
    version = "1.0.0",
    mode = "local",
    rawEscPosEnabled = options.AllowRawEscPos
}));
GenericPrintEndpoints.Map(app, printOptions);
app.Run();

public partial class Program;
