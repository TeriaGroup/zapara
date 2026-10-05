using System.Net;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserEditorStateTests
{
    [Fact]
    public async Task Stale_editor_cannot_overwrite_another_tabs_persisted_value()
    {
        var disk = new MemoryBrowser();
        using var http = new HttpClient { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk);
        var first = new WebAppState(http, storage);
        var second = new WebAppState(http, storage);
        var id = Guid.NewGuid();
        var original = new FriendValue("1", "О3313", "Иван", 1, true);
        await first.PutAsync("friend", id, original);
        await second.PutAsync("friend", id, new FriendValue("1", "О3313", "Из другой вкладки", 1, true));

        var error = await Assert.ThrowsAsync<BrowserApiException>(() => first.PutEditorAsync("guest", 0,
            "friend", id, new FriendValue("1", "О3313", "Старый редактор", 1, true), original, 0));

        Assert.Equal("editor_changed", error.Code);
        Assert.Equal("Из другой вкладки", ProfileValues.Read<FriendValue>(disk.Load<WebProfile>("profiles", "guest")!
            .Records[ProfileValues.Key("friend", id)])!.MemberNames);
    }

    [Fact]
    public async Task New_editor_cannot_resurrect_a_deleted_entity_and_wrong_owner_cannot_write()
    {
        var disk = new MemoryBrowser();
        using var http = new HttpClient { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk);
        var state = new WebAppState(http, storage);
        var id = Guid.NewGuid();
        await state.PutAsync("friend", id, null);
        var value = new FriendValue("1", "О3313", "Иван", 1, true);

        await Assert.ThrowsAsync<BrowserApiException>(() => state.PutEditorAsync("guest", 0, "friend", id, value, null, null));
        await Assert.ThrowsAsync<BrowserApiException>(() => state.PutEditorAsync("account@https://zapara.test#other", 0,
            "friend", Guid.NewGuid(), value, null, null));

        Assert.True(disk.Load<WebProfile>("profiles", "guest")!.Records[ProfileValues.Key("friend", id)].Tombstone);
        Assert.Empty(state.Values<FriendValue>("friend"));
    }

    [Fact]
    public async Task Reload_barrier_waits_for_pending_drafts_and_their_storage_writes()
    {
        var disk = new MemoryBrowser();
        using var http = new HttpClient();
        await using var storage = new BrowserStorage(disk);
        var state = new WebAppState(http, storage);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.BeforeReload += async ct => { entered.SetResult(); await released.Task.WaitAsync(ct); await storage.WriteAsync("preferences", "draft", "Сохранено"); };

        var barrier = state.WaitForStorageAsync(TestContext.Current.CancellationToken);
        await Task.WhenAny(barrier, entered.Task);
        Assert.True(entered.Task.IsCompleted, "Reload must start draft flushing before the storage barrier completes.");
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(barrier.IsCompleted);
        released.SetResult();
        await barrier;

        Assert.Equal("Сохранено", disk.Load<string>("preferences", "draft"));
    }
}
