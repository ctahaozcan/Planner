using System.IO;
using System.Windows.Media.Imaging;
using Planner.Chat;
using Planner.Core;
using Planner.Core.Models;

namespace Planner.App.Services;

public static class ChatImaging
{
    public static string PrepareBody(string path)
    {
        var data = File.ReadAllBytes(path);
        var jpeg = PortraitImaging.EncodeJpeg(PortraitImaging.DecodeFrozen(data, 480), 42);
        var name = Guid.NewGuid().ToString("N") + ".jpg";
        Directory.CreateDirectory(AppPaths.ChatMediaDirectory);
        File.WriteAllBytes(Path.Combine(AppPaths.ChatMediaDirectory, name), jpeg);
        return CollabPayload.ImageBody(name, Convert.ToBase64String(jpeg));
    }

    public static string? Materialize(string body)
    {
        var (name, encoded) = CollabPayload.ParseImage(body);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.ChatMediaDirectory);
        var dest = Path.Combine(AppPaths.ChatMediaDirectory, Path.GetFileName(name));
        if (!string.IsNullOrEmpty(encoded) && !File.Exists(dest))
        {
            try
            {
                File.WriteAllBytes(dest, Convert.FromBase64String(encoded));
            }
            catch
            {
                return File.Exists(dest) ? dest : null;
            }
        }

        return File.Exists(dest) ? dest : null;
    }

    public static string PrepareFileBody(string path)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length > ChatRoutes.MaxFileBytes)
        {
            throw new InvalidOperationException("Dosya en fazla 400 KB olabilir.");
        }

        var ext = Path.GetExtension(path);
        if (ext.Length > 12)
        {
            ext = "";
        }

        var original = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(original))
        {
            original = "dosya";
        }

        original = original.Replace('|', '_');
        var stored = Guid.NewGuid().ToString("N") + ext;
        Directory.CreateDirectory(AppPaths.ChatMediaDirectory);
        File.WriteAllBytes(Path.Combine(AppPaths.ChatMediaDirectory, stored), data);
        return CollabPayload.FileBody(original + ":" + stored, Convert.ToBase64String(data));
    }

    public static string? MaterializeFile(string body)
    {
        var (label, encoded) = CollabPayload.ParseFile(body);
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        var stored = label;
        var colon = label.LastIndexOf(':');
        if (colon > 0)
        {
            stored = label[(colon + 1)..];
        }

        Directory.CreateDirectory(AppPaths.ChatMediaDirectory);
        var dest = Path.Combine(AppPaths.ChatMediaDirectory, Path.GetFileName(stored));
        if (!string.IsNullOrEmpty(encoded) && !File.Exists(dest))
        {
            try
            {
                File.WriteAllBytes(dest, Convert.FromBase64String(encoded));
            }
            catch
            {
                return File.Exists(dest) ? dest : null;
            }
        }

        return File.Exists(dest) ? dest : null;
    }

    public static string FileDisplayName(string body)
    {
        var (label, _) = CollabPayload.ParseFile(body);
        var colon = label.LastIndexOf(':');
        return colon > 0 ? label[..colon] : (string.IsNullOrWhiteSpace(label) ? "dosya" : label);
    }

    public static BitmapImage? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
