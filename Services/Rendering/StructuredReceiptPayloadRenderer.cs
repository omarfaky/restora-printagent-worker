using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;

namespace SilentPrintAgent.Services.Rendering;

public sealed class StructuredReceiptPayloadRenderer(EscPosRenderer renderer) : IPayloadRenderer
{
    public bool CanRender(PrintPayloadKind kind) => kind == PrintPayloadKind.StructuredReceipt;
    public Task<byte[]> RenderAsync(PrintPayload payload, PrinterProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload.Json)) throw new PrinterException(PrinterException.Codes.RenderError, "Receipt payload is empty.");
        return Task.FromResult(renderer.RenderReceipt(payload.Json, profile.ToLegacy()));
    }
}
