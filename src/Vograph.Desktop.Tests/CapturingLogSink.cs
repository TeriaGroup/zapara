using Avalonia.Logging;

namespace Vograph.Desktop.Tests;

/// <summary>Collects Avalonia binding/property warnings so a view with a broken binding fails its test.</summary>
public sealed class CapturingLogSink : ILogSink
{
    public List<string> Warnings { get; } = new();

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, Array.Empty<object?>());

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (level < LogEventLevel.Warning) return;
        if (area != LogArea.Binding && area != LogArea.Property) return;
        Warnings.Add($"{area}: {messageTemplate} [{string.Join(", ", propertyValues.Select(v => v?.ToString()))}] source={source?.GetType().Name}");
    }
}
