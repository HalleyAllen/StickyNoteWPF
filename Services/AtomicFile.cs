using System.IO;
using System.Text;

namespace StickyNoteWPF.Services;

public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
                {
                    writer.Write(contents);
                    writer.Flush();
                }
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, fullPath + ".bak");
            else
                File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public static string PreserveCorruptedFile(string path)
    {
        string backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
            + "-" + Guid.NewGuid().ToString("N") + ".json";
        File.Copy(path, backup, overwrite: false);
        return backup;
    }
}
