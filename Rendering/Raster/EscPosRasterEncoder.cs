using SkiaSharp;

namespace SilentPrintAgent.Rendering.Raster;

/// <summary>
/// يحول صورة أحادية اللون إلى أوامر الطباعة النقطية GS v 0.
/// </summary>
/// <remarks>
/// صيغة الأمر: <c>1D 76 30 m xL xH yL yH d1..dk</c>
/// حيث xL/xH عدد البايتات في الصف (العرض/8)، وyL/yH عدد الصفوف،
/// وكل بت مضبوط يعني نقطة سوداء، وأعلى بت في البايت هو أقصى يسار الصف.
/// </remarks>
public sealed class EscPosRasterEncoder
{
    /// <summary>
    /// أقصى عدد صفوف في الأمر الواحد. بعض الموديلات الرخيصة لا تحتمل صورة
    /// كاملة في أمر واحد فتطبع نصف إيصال؛ التقسيم يحافظ على التوافق.
    /// </summary>
    internal static int MaxBandHeight => ReceiptLayout.MaxBandHeight;

    /// <summary>عتبة السواد: النقطة سوداء لو متوسط السطوع أقل منها.</summary>
    internal static int DarkThreshold => ReceiptLayout.DarkThreshold;

    public byte[] Encode(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (bitmap.Width % 8 != 0)
            throw new ArgumentException(
                $"عرض الصورة يجب أن يقبل القسمة على 8، والقيمة {bitmap.Width}.",
                nameof(bitmap));

        var bytesPerRow = bitmap.Width / 8;
        var output = new List<byte>(bytesPerRow * bitmap.Height + 64);

        for (var top = 0; top < bitmap.Height; top += MaxBandHeight)
        {
            var bandHeight = Math.Min(MaxBandHeight, bitmap.Height - top);

            output.AddRange([0x1D, 0x76, 0x30, 0x00]);           // GS v 0, m = 0
            output.Add((byte)(bytesPerRow & 0xFF));               // xL
            output.Add((byte)((bytesPerRow >> 8) & 0xFF));        // xH
            output.Add((byte)(bandHeight & 0xFF));                // yL
            output.Add((byte)((bandHeight >> 8) & 0xFF));         // yH

            for (var y = top; y < top + bandHeight; y++)
                AppendRow(output, bitmap, y, bytesPerRow);
        }

        return [.. output];
    }

    private static void AppendRow(List<byte> output, SKBitmap bitmap, int y, int bytesPerRow)
    {
        for (var byteIndex = 0; byteIndex < bytesPerRow; byteIndex++)
        {
            byte packed = 0;

            for (var bit = 0; bit < 8; bit++)
            {
                if (!IsDark(bitmap.GetPixel((byteIndex * 8) + bit, y)))
                    continue;

                // أعلى بت = أقصى يسار الصف؛ عكسها ينتج صورة مرآة.
                packed |= (byte)(0x80 >> bit);
            }

            output.Add(packed);
        }
    }

    /// <summary>النقطة سوداء لو متوسط القنوات أقل من العتبة. البكسل الشفاف يعتبر أبيض.</summary>
    internal static bool IsDark(SKColor color) =>
        color.Alpha >= DarkThreshold && (color.Red + color.Green + color.Blue) / 3 < DarkThreshold;
}
