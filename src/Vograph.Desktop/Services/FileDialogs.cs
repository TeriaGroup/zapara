using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Vograph.Desktop.Services;

/// <summary>Save/Open JSON pickers behind an interface so view models and tests never open OS dialogs.</summary>
public interface IFileDialogs
{
    Task<string?> SaveJsonAsync(string suggestedName);
    Task<string?> OpenJsonAsync();
}

/// <summary>Default slot before App installs the real pickers: every dialog reads as "cancelled".</summary>
public sealed class NullFileDialogs : IFileDialogs
{
    public Task<string?> SaveJsonAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
}

public sealed class AvaloniaFileDialogs : IFileDialogs
{
    private static readonly FilePickerFileType Json = new("JSON") { Patterns = new[] { "*.json" } };
    private readonly Func<TopLevel?> _topLevel;

    public AvaloniaFileDialogs(Func<TopLevel?> topLevel) => _topLevel = topLevel;

    public async Task<string?> SaveJsonAsync(string suggestedName)
    {
        if (_topLevel() is not { } tl) return null;
        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { Json },
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenJsonAsync()
    {
        if (_topLevel() is not { } tl) return null;
        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = new[] { Json } });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }
}
