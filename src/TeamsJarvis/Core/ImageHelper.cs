using System.IO;
using System.Windows.Media.Imaging;

namespace TeamsJarvis.Core;

/// <summary>Reescala imágenes grandes antes de enviarlas a Claude (límite práctico ~2 MB / 1568 px).</summary>
public static class ImageHelper
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxEdge = 1568;

    public static (byte[] Bytes, string MediaType) EnsureWithinLimits(byte[] bytes, string mediaType)
    {
        try
        {
            if (bytes.Length <= MaxBytes) return (bytes, NormalizeMediaType(mediaType));

            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            double scale = Math.Min(1.0, (double)MaxEdge / Math.Max(frame.PixelWidth, frame.PixelHeight));
            // Aunque la imagen ya quepa en píxeles, reencodar a JPEG reduce el peso.
            var transformed = scale < 1.0
                ? new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale))
                : (BitmapSource)frame;

            var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
            encoder.Frames.Add(BitmapFrame.Create(transformed));
            using var output = new MemoryStream();
            encoder.Save(output);
            return (output.ToArray(), "image/jpeg");
        }
        catch
        {
            // Si algo falla, devolvemos la original; la API rechazará solo si excede sus límites.
            return (bytes, NormalizeMediaType(mediaType));
        }
    }

    private static string NormalizeMediaType(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/jpg" => "image/jpeg",
        "image/jpeg" or "image/png" or "image/gif" or "image/webp" => mediaType.ToLowerInvariant(),
        _ => "image/png",
    };
}
