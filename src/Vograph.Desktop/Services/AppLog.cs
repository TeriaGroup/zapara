namespace Vograph.Desktop.Services;

/// <summary>Plain daily log file. Logging must never throw or block the UI for long.</summary>
public sealed class AppLog
{
    private readonly string _dir;
    private readonly object _gate = new();

    public AppLog(string dir) => _dir = dir;

    public string CurrentFile => Path.Combine(_dir, $"desktop-{DateTime.Now:yyyyMMdd}.log");

    public IReadOnlyList<(string Name, byte[] Bytes)> ReadFiles()
    {
        try
        {
            if (!Directory.Exists(_dir)) return [];
            var paths = Directory.EnumerateFiles(_dir)
                .Where(IsLogName)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var chosen = new List<(string Name, byte[] Bytes)>();
            long total = 0;
            const int cap = 2 * 1024 * 1024;
            for (var i = paths.Length - 1; i >= 0 && total < cap; i--)
            {
                var bytes = ReadTail(paths[i], cap - total);
                if (bytes.Length == 0) continue;
                chosen.Add((Path.GetFileName(paths[i]), bytes));
                total += bytes.Length;
            }
            chosen.Reverse();
            return chosen;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static bool IsLogName(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".log", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadTail(string path, long take)
    {
        if (take <= 0) return [];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length > take) stream.Seek(stream.Length - take, SeekOrigin.Begin);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string context, Exception ex) =>
        Write("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            lock (_gate)
            {
                File.AppendAllText(CurrentFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Disk full / locked file: dropping a log line is acceptable, crashing the app is not.
        }
    }
}
