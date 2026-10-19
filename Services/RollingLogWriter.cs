using System.IO;
using System.Text;

namespace StickyNoteWPF.Services;

public sealed class RollingLogWriter
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _backups;

    public RollingLogWriter(string path, long maxBytes = 1024 * 1024, int backups = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(backups, 1);
        _path = path;
        _maxBytes = maxBytes;
        _backups = backups;
    }

    public void Append(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) > _maxBytes)
            line = line[..(int)Math.Min(line.Length, (_maxBytes - 32) / 4)] + " [truncated]" + Environment.NewLine;
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            if (File.Exists(_path) && new FileInfo(_path).Length + Encoding.UTF8.GetByteCount(line) > _maxBytes)
            {
                for (int i = _backups; i >= 1; i--)
                {
                    string source = i == 1 ? _path : _path + "." + (i - 1);
                    string target = _path + "." + i;
                    if (File.Exists(source)) File.Move(source, target, overwrite: true);
                }
            }
            File.AppendAllText(_path, line, new UTF8Encoding(false));
        }
    }
}
