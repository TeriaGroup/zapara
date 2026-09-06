using Vograph.Desktop.Services;

namespace Vograph.Desktop.Tests;

/// <summary>Scripted Save/Open pickers: the view model gets the paths a user would have picked, no OS dialog opens.</summary>
public sealed class FakeFileDialogs : IFileDialogs
{
    public string? SavePath { get; set; }
    public string? OpenPath { get; set; }
    public string? LastSuggestedName { get; private set; }

    public Task<string?> SaveJsonAsync(string suggestedName)
    {
        LastSuggestedName = suggestedName;
        return Task.FromResult(SavePath);
    }

    public Task<string?> OpenJsonAsync() => Task.FromResult(OpenPath);
}
