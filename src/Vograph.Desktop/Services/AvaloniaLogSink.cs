using Avalonia.Logging;

namespace Vograph.Desktop.Services;

/// <summary>Forwards Avalonia warnings (binding errors, missing resources) into the app log.</summary>
public sealed class AvaloniaLogSink : ILogSink
{
    private readonly AppLog _log;

    public AvaloniaLogSink(AppLog log) => _log = log;

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, Array.Empty<object?>());

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        var values = propertyValues.Length == 0 ? "" : " [" + string.Join(", ", propertyValues.Select(v => v?.ToString() ?? "null")) + "]";
        _log.Warn($"avalonia/{area} ({source?.GetType().Name ?? "-"}): {messageTemplate}{values}");
    }
}
