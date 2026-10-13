using SilentPrintAgent.Configuration;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;

namespace SilentPrintAgent.Services.Rendering;

public sealed class RawEscPosPayloadRenderer(PrintAgentOptions options) : IPayloadRenderer
{
    public bool CanRender(PrintPayloadKind kind) => kind == PrintPayloadKind.RawEscPos;
    public Task<byte[]> RenderAsync(PrintPayload payload, PrinterProfile profile, CancellationToken ct)
    {
        if (!options.AllowRawEscPos) throw new PrinterException("RawEscPosDisabled", "Raw ESC/POS is disabled.");
        if (payload.Bytes is not { Length: > 0 }) throw new PrinterException("InvalidPayload", "Raw payload is empty.");
        if (payload.Bytes.Length > options.MaxRawEscPosBytes) throw new PrinterException("PayloadTooLarge", "Raw payload exceeds the configured size limit.");
        return Task.FromResult(payload.Bytes);
    }
}
