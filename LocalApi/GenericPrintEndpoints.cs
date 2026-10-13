using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SilentPrintAgent.Configuration;
using SilentPrintAgent.Core.Contracts;
using SilentPrintAgent.Core.Models;

namespace SilentPrintAgent.LocalApi;

public static class GenericPrintEndpoints
{
    private sealed record ReceiptPrintRequest(string JobId, string PrinterId, JsonElement Receipt, byte? Copies, bool? OpenCashDrawer);
    private sealed record BinaryPrintMetadata(string JobId, string PrinterId, byte? Copies, bool? OpenCashDrawer);

    public static void Map(WebApplication app, PrintAgentOptions options)
    {
        app.MapGet("/api/v1/printers", async (HttpContext ctx, IPrinterResolver printers, PrintAgentOptions options, CancellationToken ct) =>
        {
            if (!Authorize(ctx, options)) return Results.Unauthorized();
            var list = await printers.ListAsync(ct);
            return Results.Ok(list.Select(p => new { p.PrinterId, p.DisplayName, p.ConnectionType, p.PaperWidthMm, p.DefaultCopies }));
        });
        app.MapPost("/api/v1/print/receipt", SubmitReceiptAsync);
        app.MapPost("/api/v1/print/image", (HttpContext c, IPrintJobStore s, IPrinterResolver p, IPrintQueue q, PrintAgentOptions o, CancellationToken ct) => SubmitBinaryAsync(c, s, p, q, o, PrintPayloadKind.Image, ct));
        app.MapPost("/api/v1/print/raw", (HttpContext c, IPrintJobStore s, IPrinterResolver p, IPrintQueue q, PrintAgentOptions o, CancellationToken ct) => SubmitBinaryAsync(c, s, p, q, o, PrintPayloadKind.RawEscPos, ct));
        app.MapGet("/api/v1/jobs/{jobId}", async (string jobId, HttpContext ctx, IPrintJobStore store, PrintAgentOptions options, CancellationToken ct) =>
        {
            if (!Authorize(ctx, options)) return Results.Unauthorized();
            var job = await store.GetAsync(options.ClientId, jobId, ct);
            return job is null ? Results.NotFound() : JobResponse(job);
        });
        app.MapPost("/api/v1/jobs/{jobId}/cancel", async (string jobId, HttpContext ctx, IPrintJobStore store, PrintAgentOptions options, CancellationToken ct) =>
        {
            if (!Authorize(ctx, options)) return Results.Unauthorized();
            var current = await store.GetAsync(options.ClientId, jobId, ct);
            if (current is null) return Results.NotFound();
            var cancelled = await store.TryCancelPendingAsync(options.ClientId, jobId, ct);
            return cancelled is null ? Results.Conflict(new { errorCode = "JobAlreadyPrinting", message = "Only pending or retrying jobs can be cancelled." }) : JobResponse(cancelled);
        });
    }

    private static async Task<IResult> SubmitReceiptAsync(ReceiptPrintRequest request, HttpContext ctx, IPrintJobStore store, IPrinterResolver printers, IPrintQueue queue, PrintAgentOptions options, CancellationToken ct)
    {
        if (!Authorize(ctx, options)) return Results.Unauthorized();
        var receipt = request.Receipt.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : CanonicalJson(request.Receipt);
        if (receipt is null) return Problem("InvalidPayload", "Receipt is required.", StatusCodes.Status400BadRequest);
        if (Encoding.UTF8.GetByteCount(receipt) > options.MaxReceiptJsonBytes) return Problem("PayloadTooLarge", "Receipt exceeds the configured size limit.", StatusCodes.Status413PayloadTooLarge);
        return await SubmitAsync(request.JobId, request.PrinterId, request.Copies, request.OpenCashDrawer, new PrintPayload(PrintPayloadKind.StructuredReceipt, receipt, null, "application/json"), store, printers, queue, options, ct);
    }

    private static async Task<IResult> SubmitBinaryAsync(HttpContext ctx, IPrintJobStore store, IPrinterResolver printers, IPrintQueue queue, PrintAgentOptions options, PrintPayloadKind kind, CancellationToken ct)
    {
        if (!Authorize(ctx, options)) return Results.Unauthorized();
        if (kind == PrintPayloadKind.RawEscPos && !options.AllowRawEscPos) return Problem("RawEscPosDisabled", "Raw ESC/POS is disabled.", StatusCodes.Status403Forbidden);
        if (!ctx.Request.HasFormContentType) return Problem("InvalidContentType", "Use multipart/form-data with metadata and file.", StatusCodes.Status415UnsupportedMediaType);
        IFormCollection form;
        try { form = await ctx.Request.ReadFormAsync(ct); }
        catch (InvalidDataException) { return Problem("InvalidMultipart", "Multipart request is malformed or exceeds its size limit.", StatusCodes.Status400BadRequest); }
        var metadataPart = form["metadata"].FirstOrDefault(); var file = form.Files.GetFile("file");
        if (string.IsNullOrWhiteSpace(metadataPart) || file is null) return Problem("InvalidPayload", "Both metadata and file are required.", StatusCodes.Status400BadRequest);
        BinaryPrintMetadata? metadata;
        try { metadata = JsonSerializer.Deserialize<BinaryPrintMetadata>(metadataPart, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return Problem("InvalidMetadata", "metadata must be valid JSON.", StatusCodes.Status400BadRequest); }
        if (metadata is null) return Problem("InvalidMetadata", "metadata is required.", StatusCodes.Status400BadRequest);
        var limit = kind == PrintPayloadKind.Image ? options.MaxImageBytes : options.MaxRawEscPosBytes;
        if (ctx.Request.ContentLength is long requestLength && requestLength > limit + 65536L)
            return Problem("PayloadTooLarge", "Request exceeds the configured size limit.", StatusCodes.Status413PayloadTooLarge);
        if (file.Length <= 0) return Problem("InvalidPayload", "File is empty.", StatusCodes.Status400BadRequest);
        if (file.Length > limit) return Problem("PayloadTooLarge", "File exceeds the configured size limit.", StatusCodes.Status413PayloadTooLarge);
        await using var stream = file.OpenReadStream(); using var buffer = new MemoryStream((int)file.Length); await stream.CopyToAsync(buffer, ct);
        var contentType = kind == PrintPayloadKind.RawEscPos ? "application/octet-stream" : file.ContentType.ToLowerInvariant();
        if (kind == PrintPayloadKind.Image && contentType is not ("image/png" or "image/jpeg")) return Problem("UnsupportedImageFormat", "Only image/png and image/jpeg are supported.", StatusCodes.Status415UnsupportedMediaType);
        return await SubmitAsync(metadata.JobId, metadata.PrinterId, metadata.Copies, metadata.OpenCashDrawer, new PrintPayload(kind, null, buffer.ToArray(), contentType), store, printers, queue, options, ct);
    }

    private static async Task<IResult> SubmitAsync(string? jobId, string? printerId, byte? requestedCopies, bool? openDrawer, PrintPayload payload, IPrintJobStore store, IPrinterResolver printers, IPrintQueue queue, PrintAgentOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 128 || !jobId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-')) return Problem("InvalidJobId", "JobId must contain 1-128 letters, digits, '.', '_', ':' or '-'.", StatusCodes.Status400BadRequest);
        if (string.IsNullOrWhiteSpace(printerId)) return Problem("InvalidPrinterId", "PrinterId is required.", StatusCodes.Status400BadRequest);
        var profile = await printers.ResolveAsync(printerId, ct);
        if (profile is null || !profile.Enabled) return Problem("PrinterNotConfigured", "Printer is not configured or enabled.", StatusCodes.Status422UnprocessableEntity);
        var copies = requestedCopies ?? profile.DefaultCopies;
        if (copies == 0 || copies > options.MaxCopies) return Problem("InvalidCopies", "Copies must be between 1 and the configured maximum.", StatusCodes.Status422UnprocessableEntity);
        payload = payload with { Copies = copies, OpenCashDrawer = openDrawer ?? false };
        var hash = Hash(payload, profile.PrinterId);
        try
        {
            var job = await store.EnqueueAsync(new PrintSubmission(jobId, options.ClientId, hash, profile.PrinterId, profile, payload, options.MaxAttempts), ct);
            await queue.SignalAsync(ct);
            return Results.Accepted($"/api/v1/jobs/{Uri.EscapeDataString(job.JobId)}", Response(job));
        }
        catch (PrintJobConflictException) { return Results.Conflict(new { errorCode = "IdempotencyConflict", message = "JobId was already used with different content." }); }
    }

    private static bool Authorize(HttpContext context, PrintAgentOptions options) => !string.IsNullOrWhiteSpace(options.LocalToken) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(options.LocalToken), Encoding.UTF8.GetBytes(context.Request.Headers["X-Agent-Token"].ToString()));
    private static IResult JobResponse(PrintJob job) => Results.Ok(Response(job));
    private static object Response(PrintJob job) => new { job.JobId, status = job.Status.ToString(), deliveryState = job.DeliveryState.ToString(), statusUrl = $"/api/v1/jobs/{Uri.EscapeDataString(job.JobId)}", job.ErrorCode, job.ErrorMessage };
    private static IResult Problem(string code, string detail, int status) => Results.Problem(detail, statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
    private static string Hash(PrintPayload p, string printer) { using var sha = SHA256.Create(); var content = p.Bytes ?? Encoding.UTF8.GetBytes(p.Json!); var envelope = Encoding.UTF8.GetBytes($"v1\n{p.Kind}\n{p.ContentType}\n{printer}\n{p.Copies}\n{p.OpenCashDrawer}\n"); var all = new byte[envelope.Length + content.Length]; Buffer.BlockCopy(envelope, 0, all, 0, envelope.Length); Buffer.BlockCopy(content, 0, all, envelope.Length, content.Length); return Convert.ToHexString(sha.ComputeHash(all)); }
    private static string CanonicalJson(JsonElement element) { using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, element); return Encoding.UTF8.GetString(stream.ToArray()); }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value) { switch (value.ValueKind) { case JsonValueKind.Object: writer.WriteStartObject(); foreach (var p in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); WriteCanonical(writer, p.Value); } writer.WriteEndObject(); break; case JsonValueKind.Array: writer.WriteStartArray(); foreach (var i in value.EnumerateArray()) WriteCanonical(writer, i); writer.WriteEndArray(); break; default: value.WriteTo(writer); break; } }
}
