using System.Text.Json;
using Microsoft.JSInterop;

namespace Zapara.Web.Services;

public sealed partial class BrowserStorage
{
    /// <summary>Owner-scoped private cache/draft write; epoch was captured before the operation began.</summary>
    public async Task WriteProfilePreferenceAsync<T>(string owner, long expectedResetEpoch, string key, T value, CancellationToken ct = default)
    {
        ValidateOwner(owner);
        if (expectedResetEpoch is < 0 or > MaximumStorageRevision) throw new BrowserStorageResetException();
        ct.ThrowIfCancellationRequested();
        var bridge = await Module;
        ct.ThrowIfCancellationRequested();
        if (!await bridge.InvokeAsync<bool>("writeProfilePreference", owner, expectedResetEpoch, key, JsonSerializer.Serialize(value, Json)))
            throw new BrowserStorageResetException();
    }
}
