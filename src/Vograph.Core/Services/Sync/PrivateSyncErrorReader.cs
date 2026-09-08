using System.Text.Json.Serialization;
using Zapara.Contracts.Sync;

namespace Vograph.Core.Services.Sync;

internal static class PrivateSyncErrorReader
{
    internal static SyncError Parse(byte[] bytes)
    {
        // Accounts middleware adds a title; Sync read/input errors contain only status/code.
        // Both pass the shared strict UTF-8, duplicate-member and depth validator.
        var error = SyncJson.Parse<WireError>(bytes);
        return new SyncError(error.Status, error.Code);
    }

    private sealed class WireError
    {
        [JsonRequired] public int Status { get; init; }
        [JsonRequired] public string Code { get; init; } = "";
        public string? Title { get; init; }
    }
}
