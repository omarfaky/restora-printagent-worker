using SilentPrintAgent.Configuration;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;
using SilentPrintAgent.Rendering.Raster;
using SkiaSharp;

namespace SilentPrintAgent.Services.Rendering;

public sealed class ImagePayloadRenderer(PrintAgentOptions options) : IPayloadRenderer
{
    private readonly EscPosRasterEncoder _encoder = new();
    public bool CanRender(PrintPayloadKind kind) => kind == PrintPayloadKind.Image;

    public Task<byte[]> RenderAsync(PrintPayload payload, PrinterProfile profile, CancellationToken ct)
    {
        if (payload.Bytes is not { Length: > 0 }) throw new PrinterException("InvalidPayload", "Image file is empty.");
        if (payload.Bytes.Length > options.MaxImageBytes) throw new PrinterException("PayloadTooLarge", "Image exceeds the configured size limit.");
        if (payload.ContentType is not ("image/png" or "image/jpeg")) throw new PrinterException("UnsupportedImageFormat", "Only PNG and JPEG images are supported.");
        using var imageData = SKData.CreateCopy(payload.Bytes);
        using var codec = SKCodec.Create(imageData);
        if (codec is null) throw new PrinterException("InvalidPayload", "Image cannot be decoded.");
        if (codec.Info.Width > options.MaxImageWidthPx || codec.Info.Height > options.MaxImageHeightPx)
            throw new PrinterException("ImageDimensionsExceeded", "Image dimensions exceed the configured limit.");
        using var source = SKBitmap.Decode(codec);
        if (source is null) throw new PrinterException("InvalidPayload", "Image cannot be decoded.");
        var width = profile.PaperWidthMm switch { 58 => 384, 80 => 576, _ => throw new PrinterException("UnsupportedPaperWidth", "Paper width must be 58 or 80 mm.") };
        var height = Math.Max(1, (int)Math.Ceiling(source.Height * (width / (double)source.Width)));
        if (height > options.MaxImageHeightPx) throw new PrinterException("ImageDimensionsExceeded", "Scaled image is too tall.");
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(source, new SKRect(0, 0, width, height)); }
        var encoded = _encoder.Encode(bitmap);
        var result = new List<byte>(encoded.Length + 8); result.AddRange([0x1B, 0x40]); result.AddRange(encoded); result.AddRange([0x1D, 0x56, 0x41, 24]);
        return Task.FromResult(result.ToArray());
    }
}
