using SilentPrintAgent.Configuration;
using SilentPrintAgent.Core.Models;
using SilentPrintAgent.Models;
using SilentPrintAgent.Services;
using SilentPrintAgent.Services.Rendering;
using SkiaSharp;
using Xunit;

namespace SilentPrintAgent.Tests;

public sealed class RendererTests
{
    private static readonly PrinterProfile Profile = new("receipt", "Receipt", AgentConnectionType.NetworkEscPos,
        null, "127.0.0.1", 9100, 58, null, 1, false, true);

    [Fact]
    public async Task Png_is_scaled_and_encoded_as_escpos_raster()
    {
        using var bitmap = new SKBitmap(80, 40);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            canvas.DrawRect(5, 5, 30, 20, paint);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var options = new PrintAgentOptions();
        var renderer = new ImagePayloadRenderer(options);
        var output = await renderer.RenderAsync(new PrintPayload(PrintPayloadKind.Image, null, png.ToArray(), "image/png"), Profile, CancellationToken.None);

        Assert.True(output.Length > 8);
        Assert.Equal(new byte[] { 0x1B, 0x40 }, output[..2]);
        Assert.Equal(new byte[] { 0x1D, 0x76, 0x30 }, output[2..5]);
    }

    [Fact]
    public async Task Invalid_and_over_dimension_images_are_rejected()
    {
        var renderer = new ImagePayloadRenderer(new PrintAgentOptions { MaxImageWidthPx = 10 });
        await Assert.ThrowsAsync<PrinterException>(() => renderer.RenderAsync(
            new PrintPayload(PrintPayloadKind.Image, null, [1, 2, 3], "image/png"), Profile, CancellationToken.None));

        using var bitmap = new SKBitmap(11, 4);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var error = await Assert.ThrowsAsync<PrinterException>(() => renderer.RenderAsync(
            new PrintPayload(PrintPayloadKind.Image, null, png.ToArray(), "image/png"), Profile, CancellationToken.None));
        Assert.Equal("ImageDimensionsExceeded", error.ErrorCode);
    }

    [Fact]
    public void Structured_receipt_renders_arabic_payload_to_bytes()
    {
        const string json = """{"cafeName":"مقهى","orderType":"DineIn","orderNumber":104,"createdAt":"2026-10-14T18:30:00+02:00","items":[],"totalAmount":0,"payments":[]}""";
        var output = new EscPosRenderer().RenderReceipt(json, Profile.ToLegacy());
        Assert.True(output.Length > 100);
        Assert.Equal(new byte[] { 0x1B, 0x40 }, output[..2]);
    }
}
