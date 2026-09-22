using System.Text.Json;
using Microsoft.JSInterop;

namespace Zapara.Web.Services;

public sealed partial class BrowserStorage(IJSRuntime js) : IAsyncDisposable
{
    private Task<IJSObjectReference>? module;
    private Task<IJSObjectReference> Module => module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/storage.js").AsTask();
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task DropHeavyLocalCopiesAsync()
    {
        try { await (await Module).InvokeVoidAsync("dropHeavyLocalCopies"); }
        catch (JSException) { }
    }

    public async Task<T?> ReadAsync<T>(string store, string key)
    {
        var text = await (await Module).InvokeAsync<string?>("read", store, key);
        return text is null ? default : JsonSerializer.Deserialize<T>(text, Json);
    }

    public async Task WriteAsync<T>(string store, string key, T value) =>
        await (await Module).InvokeVoidAsync("write", store, key, JsonSerializer.Serialize(value, Json));

    public async Task AppearanceAsync(string theme, bool animations) =>
        await (await Module).InvokeVoidAsync("appearance", theme, animations);

    public async ValueTask DisposeAsync()
    {
        if (module is not null && module.IsCompletedSuccessfully) await module.Result.DisposeAsync();
    }
}
