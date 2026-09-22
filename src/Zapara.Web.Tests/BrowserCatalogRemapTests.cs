using System.Net;
using System.Net.Http.Json;
using Vograph.Core.Models;
using Zapara.Client.Domain;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserCatalogRemapTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delayed_catalog_remap_cannot_resurrect_another_tabs_deleted_friend_or_replace_its_delete()
    {
        using var h = await Harness.Open();
        var refresh = h.BeginRefresh(); await h.Server.Started.Task.WaitAsync(Ct);
        await h.B.PutAsync("friend", h.FriendId, null);
        h.Server.Release.SetResult(); await refresh;
        Assert.Empty(h.A.Values<FriendValue>("friend"));
        var disk = h.Disk.Load<WebProfile>("profiles", h.Owner)!;
        Assert.True(disk.Records[ProfileValues.Key("friend", h.FriendId)].Tombstone);
        var operation = Assert.Single(disk.Outbox, p => p.EntityType == "friend");
        Assert.Equal("delete", operation.Action); Assert.Null(operation.Value);
    }

    [Fact]
    public async Task Remapping_selected_group_preserves_other_tabs_latest_reminders_parity_and_friend_fields()
    {
        using var h = await Harness.Open();
        var refresh = h.BeginRefresh(); await h.Server.Started.Task.WaitAsync(Ct);
        await h.B.SaveSettingsAsync(new("42", true, null, "06:45", 100, true));
        await h.B.PutAsync("friend", h.FriendId, new FriendValue("42", "О3313", "Новое имя", 4, false));
        h.Server.Release.SetResult(); await refresh;
        Assert.Equal(new SettingsValue("О3313", true, null, "06:45", 100, true), h.A.Settings);
        Assert.Equal(new FriendValue("О3313", "О3313", "Новое имя", 4, false), Assert.Single(h.A.Values<FriendValue>("friend")).Value);
        var disk = h.Disk.Load<WebProfile>("profiles", h.Owner)!;
        Assert.Equal(h.A.Settings, ProfileValues.Read<SettingsValue>(disk.Records[ProfileValues.Key("settings", SyncValidation.SettingsId)]));
    }

    private sealed class Harness : IDisposable
    {
        public readonly MemoryBrowser Disk = new(); public readonly Catalog Server = new();
        public readonly Guid FriendId = Guid.NewGuid(); public string Owner => "account@https://zapara.test#" + Server.Family;
        public WebAppState A = null!, B = null!;
        private readonly List<HttpClient> clients = [];
        public static async Task<Harness> Open()
        {
            var h = new Harness(); var profile = new WebProfile { Owner = h.Owner };
            profile.Records[ProfileValues.Key("settings", SyncValidation.SettingsId)] = new() { EntityType = "settings", EntityId = SyncValidation.SettingsId, Revision = 5, Value = ProfileValues.Serialize(new SettingsValue("42", false, "20:00", "07:30", 25, false)) };
            profile.Records[ProfileValues.Key("friend", h.FriendId)] = new() { EntityType = "friend", EntityId = h.FriendId, Revision = 7, Value = ProfileValues.Serialize(new FriendValue("42", "О3313", "Иван", 1, true)) };
            h.Disk.Store("profiles", h.Owner, profile);
            h.Disk.Store("public", "schedule:" + h.Owner, new PublicCache(new ScheduleSnapshot(new(2026, 9, 1), 2, [new Group { Id = "42", Name = "О3313" }], []), "Осень", true));
            h.A = await h.Create(); h.B = await h.Create(); return h;
        }
        private async Task<WebAppState> Create() { var http = new HttpClient(Server, false) { BaseAddress = new("https://zapara.test/app/") }; clients.Add(http); var storage = new BrowserStorage(Disk); var state = new WebAppState(http, storage, new BrowserApiClient(http, storage)); await state.InitializeAsync(); return state; }
        public Task BeginRefresh() { Server.Enabled = true; return A.RefreshAsync(Ct); }
        public void Dispose() { foreach (var client in clients) client.Dispose(); Server.Dispose(); }
    }
    private sealed class Catalog : HttpMessageHandler
    {
        public readonly Guid Family = Guid.NewGuid(), Snapshot = Guid.NewGuid();
        public bool Enabled;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/web-api/session") return BrowserApiClientTests.Session(Family);
            if (!Enabled) return new(HttpStatusCode.ServiceUnavailable);
            var group = new WebGroup("О3313", "О3313", 0); var period = new WebPeriod(new(2026, 9, 1), 2, "Осень", "Europe/Moscow"); var meta = new WebSnapshot(Snapshot, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false);
            if (request.RequestUri.AbsolutePath == "/api/v1/groups") return new(HttpStatusCode.OK) { Content = JsonContent.Create(new GroupsEnvelope(period, meta, [group])) };
            Started.TrySetResult(); await Release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new LessonsEnvelope(period, meta, group, [])) };
        }
    }
}
