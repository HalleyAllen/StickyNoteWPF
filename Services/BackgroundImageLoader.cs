using System.IO;
using System.Windows.Media.Imaging;

namespace StickyNoteWPF.Services;

/// <summary>Retains only the current window's image and reloads it when the file changes.</summary>
public sealed class BackgroundImageLoader
{
    private (string Path, DateTime Modified, long Length)? _stamp;
    private BitmapImage? _image;

    public BitmapImage Load(string path)
    {
        var file = new FileInfo(path);
        var stamp = (file.FullName, file.LastWriteTimeUtc, file.Length);
        if (_stamp == stamp && _image != null) return _image;

        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        _stamp = stamp;
        return _image = image;
    }
}
