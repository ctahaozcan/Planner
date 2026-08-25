using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Planner.App.Services;

public static class PortraitImaging
{
    public const int ThumbPx = 96;
    public const int PanelPx = 160;

    public static (byte[] Original, byte[] Thumb) Prepare(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Fotoğraf bulunamadı.", path);
        }

        var info = new FileInfo(path);
        if (info.Length > Planner.Core.Services.PortraitStore.MaxBytes)
        {
            throw new InvalidOperationException("Fotoğraf 5 MB sınırını aşıyor.");
        }

        var ext = info.Extension.ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp"))
        {
            throw new InvalidOperationException("Desteklenen biçimler: JPG, PNG, WEBP, BMP.");
        }

        var original = File.ReadAllBytes(path);
        BitmapSource decoded;
        try
        {
            decoded = DecodeFrozen(original, ThumbPx);
        }
        catch
        {
            throw new InvalidOperationException("Dosya bir fotoğraf olarak açılamadı.");
        }

        return (original, EncodeJpeg(decoded));
    }

    public static BitmapSource DecodeFrozen(byte[] data, int decodeWidth)
    {
        using var stream = new MemoryStream(data, writable: false);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (decodeWidth > 0)
        {
            bmp.DecodePixelWidth = decodeWidth;
        }

        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    public static byte[] EncodeJpeg(BitmapSource source, int quality = 80)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
