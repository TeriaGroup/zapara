namespace Vograph.Desktop.Services;

/// <summary>Plain daily log file. Logging must never throw or block the UI for long.</summary>
public sealed class AppLog
{
    private readonly string _dir;
    private readonly object _gate = new();

    public AppLog(string dir) => _dir = dir;

    public string CurrentFile => Path.Combine(_dir, $"desktop-{DateTime.Now:yyyyMMdd}.log");

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
