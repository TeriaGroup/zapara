using System.Text.Json;
using Microsoft.JSInterop;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserStorageResetEpochTests
{
    [Fact]
    public async Task Old_writer_cannot_adopt_the_epoch_of_an_already_cleared_profile()
    {
        var database = new Memory { Raw = Profile(5, 1) };
        await using var storage = new BrowserStorage(database);
        var transformed = false;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => storage.UpdateProfileAsync("guest", p => {
            transformed = true; p.Outbox.Add(new()); return p;
        }, TestContext.Current.CancellationToken));
        Assert.False(transformed);
        Assert.Equal(Profile(5, 1), database.Raw);
    }

    [Fact]
    public async Task A_clear_between_read_and_CAS_aborts_retry_instead_of_recreating_old_records()
    {
        var database = new Memory { Raw = Profile(4, 0), ResetOnFirstCompare = true };
        await using var storage = new BrowserStorage(database);
        var transforms = 0;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => storage.UpdateProfileAsync("guest", p => {
            transforms++; p.Outbox.Add(new()); return p;
        }, TestContext.Current.CancellationToken));
        Assert.Equal(1, transforms);
        Assert.Equal(Profile(5, 1), database.Raw);
    }

    private static string Profile(long revision, long epoch) => $$"""{"owner":"guest","storageRevision":{{revision}},"resetEpoch":{{epoch}},"records":{},"outbox":[]} """.Trim();
    private sealed class Memory : IJSRuntime, IJSObjectReference
    {
        public string Raw = Profile(0, 0);
        public bool ResetOnFirstCompare;
        private int compares;
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            object? result;
            if (id == "import") result = this;
            else if (id == "read") result = Raw;
            else if (id == "compareExchangeProfile")
            {
                if (ResetOnFirstCompare && compares++ == 0) Raw = Profile(5, 1);
                var expected = (long)args![1]!;
                var revision = JsonDocument.Parse(Raw).RootElement.GetProperty("storageRevision").GetInt64();
                result = revision == expected;
                if ((bool)result) Raw = (string)args[2]!;
            }
            else throw new InvalidOperationException(id);
            return ValueTask.FromResult((T)result!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
