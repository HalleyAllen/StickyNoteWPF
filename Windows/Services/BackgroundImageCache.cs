using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StickyNoteWPF.Services;

// 只弱引用解码结果；最后一个窗口关闭后图片可回收，缓存键也有数量上限。
internal static class BackgroundImageCache
{
    private readonly record struct Key(string Path, long Modified, long Length, int Width, int Height, bool Cover);
    private static readonly Dictionary<Key, WeakReference<BitmapSource>> Cache = new();

    public static BitmapSource Load(string path, int width, int height, bool originalSize, bool cover)
    {
        var file = new FileInfo(path);
        var key = new Key(file.FullName.ToUpperInvariant(), file.LastWriteTimeUtc.Ticks, file.Length,
            originalSize ? 0 : Bucket(width), originalSize ? 0 : Bucket(height), cover);
        if (Cache.TryGetValue(key, out var cached) && cached.TryGetTarget(out var existing)) return existing;

        // 从流加载，避免 WPF 的 URI 缓存额外保留原始大图；OnLoad 后立即释放文件句柄。
        int sourceWidth, sourceHeight;
        using (var metadata = Open(file.FullName))
        {
            var decoder = BitmapDecoder.Create(metadata, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            sourceWidth = decoder.Frames[0].PixelWidth;
            sourceHeight = decoder.Frames[0].PixelHeight;
        }
        using var stream = Open(file.FullName);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        if (!originalSize)
        {
            if (cover)
            {
                // UniformToFill 按覆盖窗口所需的较大缩放比例解码，保持宽高比与清晰度。
                if ((double)sourceWidth / key.Width > (double)sourceHeight / key.Height)
                    image.DecodePixelHeight = Math.Min(sourceHeight, key.Height);
                else image.DecodePixelWidth = Math.Min(sourceWidth, key.Width);
            }
            else
            {
                image.DecodePixelWidth = Math.Min(sourceWidth, key.Width);
                image.DecodePixelHeight = Math.Min(sourceHeight, key.Height);
            }
        }
        image.EndInit();
        image.Freeze();
        foreach (var dead in Cache.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray()) Cache.Remove(dead);
        if (Cache.Count >= 64) Cache.Remove(Cache.Keys.First());
        Cache[key] = new WeakReference<BitmapSource>(image);
        return image;
    }

    private static int Bucket(int value) => Math.Max(256, (int)Math.Ceiling(value / 256d) * 256);
    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
