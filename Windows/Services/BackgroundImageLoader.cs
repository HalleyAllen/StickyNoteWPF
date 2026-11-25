using System.IO;
using System.Windows.Media.Imaging;

namespace StickyNoteWPF.Services;

/// <summary>Retains only the current window's image and reloads it when the file changes.</summary>
public sealed class BackgroundImageLoader
{
    private readonly SemaphoreSlim _decodeGate = new(1);
    private readonly object _cacheGate = new();
    private (string Path, DateTime Modified, long Length, int Width, int Height)? _stamp;
    private BitmapImage? _image;
    private int _generation;

    public Task<BitmapImage> LoadAsync(string path, int pixelWidth, int pixelHeight,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        _decodeGate.Wait(cancellationToken);
        try { return Load(path, pixelWidth, pixelHeight, cancellationToken); }
        finally { _decodeGate.Release(); }
    }, cancellationToken);

    public void Clear()
    {
        lock (_cacheGate)
        {
            _generation++;
            _stamp = null;
            _image = null;
        }
    }

    private BitmapImage Load(string path, int pixelWidth, int pixelHeight, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int generation;
        lock (_cacheGate) generation = _generation;
        var file = new FileInfo(path);
        var stamp = (file.FullName, file.LastWriteTimeUtc, file.Length, pixelWidth, pixelHeight);
        lock (_cacheGate)
            if (_stamp == stamp && _image != null) return _image;

        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        int decodeWidth = 0;
        if (pixelWidth > 0 && pixelHeight > 0)
        {
            // Read dimensions without eagerly decoding the full-resolution pixel buffer.
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            double scale = Math.Min(1, Math.Max((double)pixelWidth / frame.PixelWidth, (double)pixelHeight / frame.PixelHeight));
            scale = Math.Min(scale, 4096d / Math.Max(frame.PixelWidth, frame.PixelHeight));
            decodeWidth = Math.Max(1, (int)Math.Ceiling(frame.PixelWidth * scale));
            stream.Position = 0;
        }
        token.ThrowIfCancellationRequested();
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        token.ThrowIfCancellationRequested();
        lock (_cacheGate)
        {
            if (generation == _generation)
            {
                _stamp = stamp;
                _image = image;
            }
        }
        return image;
    }
}
