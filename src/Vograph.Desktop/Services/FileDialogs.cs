using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Vograph.Desktop.Services;

/// <summary>File pickers behind an interface so view models and tests never open OS dialogs.</summary>
public interface IFileDialogs
{
    Task<string?> SaveJsonAsync(string suggestedName);
    Task<string?> OpenJsonAsync();
    Task<string?> OpenHomeworkAsync(bool photo);
    Task<string?> OpenChatMediaAsync(string kind);
    Task<string?> SaveChatMediaAsync(string suggestedName);
    Task<IReadOnlyList<string>> OpenSupportAsync(string kind);
}

/// <summary>Default slot before App installs the real pickers: every dialog reads as "cancelled".</summary>
public sealed class NullFileDialogs : IFileDialogs
{
    public Task<string?> SaveJsonAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<string?> OpenJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> OpenHomeworkAsync(bool photo) => Task.FromResult<string?>(null);
    public Task<string?> OpenChatMediaAsync(string kind) => Task.FromResult<string?>(null);
    public Task<string?> SaveChatMediaAsync(string suggestedName) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>> OpenSupportAsync(string kind) => Task.FromResult<IReadOnlyList<string>>([]);
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

    public async Task<string?> OpenHomeworkAsync(bool photo)
    {
        if (_topLevel() is not { } tl) return null;
        var kind = photo
            ? new FilePickerFileType("image") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif" } }
            : new FilePickerFileType("document") { Patterns = new[] { "*.pdf", "*.txt", "*.csv", "*.rtf", "*.doc", "*.docx", "*.xls", "*.xlsx", "*.ppt", "*.pptx", "*.odt", "*.ods", "*.odp", "*.zip" } };
        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = new[] { kind } });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> OpenChatMediaAsync(string kind)
    {
        if (_topLevel() is not { } tl) return null;
        var filter = kind switch
        {
            "image" => new FilePickerFileType("image") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.gif", "*.bmp" } },
            "video" => new FilePickerFileType("video") { Patterns = new[] { "*.mp4", "*.webm", "*.mov" } },
            _ => new FilePickerFileType("document") { Patterns = new[] { "*.pdf", "*.txt", "*.csv", "*.rtf", "*.doc", "*.docx", "*.xls", "*.xlsx", "*.ppt", "*.pptx", "*.odt", "*.ods", "*.odp", "*.zip" } }
        };
        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = new[] { filter } });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> SaveChatMediaAsync(string suggestedName)
    {
        if (_topLevel() is not { } tl) return null;
        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> OpenSupportAsync(string kind)
    {
        if (_topLevel() is not { } tl) return [];
        var filter = kind == "photo"
            ? new FilePickerFileType("image") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"] }
            : new FilePickerFileType("log") { Patterns = ["*.txt", "*.log"] };
        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true, FileTypeFilter = [filter] });
        return files.Select(file => file.TryGetLocalPath()).Where(path => !string.IsNullOrEmpty(path)).Cast<string>().ToArray();
    }
}
