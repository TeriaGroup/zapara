using System.Net;
using System.Text.Json;
using Microsoft.JSInterop;
using Vograph.Core.Models;
using Zapara.Client.Domain;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;
using Xunit;

namespace Zapara.Web.Tests;

public sealed class BrowserStateTests
{
    [Fact]
    public async Task Xml_group_ids_follow_the_same_group_when_the_server_switches_to_Json_names()
    {
        var disk = new MemoryBrowser();
        var profile = new WebProfile();
        var settings = new SettingsValue("42", false, "20:00", "07:30", 0, false);
        profile.Records[ProfileValues.Key("settings", SyncValidation.SettingsId)] = new()
        { EntityType = "settings", EntityId = SyncValidation.SettingsId, Value = ProfileValues.Serialize(settings) };
        disk.Store("profiles", "guest", profile);
        disk.Store("public", "schedule", new PublicCache(new ScheduleSnapshot(new(2026, 9, 1), 2,
            [new Group { Id = "42", Name = "О3313" }], []), "Осень", true));
        using var handler = new CatalogHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk);
        var state = new WebAppState(http, storage);

        await state.InitializeAsync();

        Assert.Equal("О3313", state.GroupId);
        Assert.Equal("О3313", state.GroupName);
        Assert.Single(state.Schedule!.Lessons);
        Assert.Equal("Высшая математика", state.Schedule.Lessons.Single().SubjectRaw);
        var restored = disk.Load<WebProfile>("profiles", "guest")!;
        Assert.Equal("О3313", ProfileValues.Read<SettingsValue>(restored.Records[ProfileValues.Key("settings", SyncValidation.SettingsId)])!.SelectedGroupId);
    }

    [Fact]
    public async Task Failed_persistence_keeps_the_previous_personal_value()
    {
        var disk = new MemoryBrowser();
        using var http = new HttpClient(new CatalogHandler()) { BaseAddress = new("https://zapara.test/app/") };
        await using var storage = new BrowserStorage(disk);
        var state = new WebAppState(http, storage);
        var id = Guid.NewGuid();
        await state.PutAsync("friend", id, new FriendValue("1", "А101", "Иван", 1, true));
        disk.FailWrites = true;

        await Assert.ThrowsAsync<JSException>(() => state.PutAsync("friend", id, new FriendValue("2", "А102", "Пётр", 2, true)));

        Assert.Equal("Иван", state.Values<FriendValue>("friend").Single().Value.MemberNames);
        Assert.Equal("А101", disk.Load<WebProfile>("profiles", "guest")!.Records[ProfileValues.Key("friend", id)].Value!.Value.GetProperty("groupName").GetString());
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string meta = "\"snapshotId\":\"11111111-1111-4111-8111-111111111111\",\"fetchedAt\":\"2026-09-21T08:00:00Z\",\"publishedAt\":\"2026-09-21T08:00:00Z\",\"stale\":false";
            const string period = "\"start\":\"2026-09-01\",\"weekCount\":2,\"title\":\"Осень\",\"timeZone\":\"Europe/Moscow\"";
            var json = request.RequestUri!.AbsolutePath == "/api/v1/groups"
                ? "{\"period\":{" + period + "},\"meta\":{" + meta + "},\"groups\":[{\"id\":\"О3313\",\"name\":\"О3313\",\"lessonCount\":1}]}"
                : "{\"period\":{" + period + "},\"meta\":{" + meta + "},\"group\":{\"id\":\"О3313\",\"name\":\"О3313\",\"lessonCount\":1},\"lessons\":[{\"dayOfWeek\":1,\"parity\":0,\"index\":1,\"timeStart\":\"09:00\",\"timeEnd\":\"10:35\",\"subjectRaw\":\"Высшая математика\",\"subjectNormalized\":\"высшая математика\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}

internal sealed class MemoryBrowser : IJSRuntime, IJSObjectReference
{
    private readonly Dictionary<string, string> values = [];
    public bool FailWrites { get; set; }
    public void Store<T>(string store, string key, T value) => values[store + ":" + key] = JsonSerializer.Serialize(value, BrowserStorage.Json);
    public T? Load<T>(string store, string key) => values.TryGetValue(store + ":" + key, out var text) ? JsonSerializer.Deserialize<T>(text, BrowserStorage.Json) : default;
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
        if (identifier == "read") return ValueTask.FromResult((TValue)(object?)values.GetValueOrDefault(args![0] + ":" + args[1])!);
        if (identifier == "write")
        {
            if (FailWrites) throw new JSException("QuotaExceededError");
            values[args![0] + ":" + args[1]] = (string)args[2]!;
        }
        if (identifier == "commitSnapshot")
        {
            if (FailWrites) throw new JSException("QuotaExceededError");
            values["profiles:" + args![0]] = (string)args[1]!;
            values["public:schedule"] = (string)args[2]!;
        }
        if (identifier is "compareExchangeProfile" or "compareExchangeProfileSnapshot")
        {
            if (FailWrites) throw new JSException("QuotaExceededError");
            var owner = (string)args![0]!;
            var expected = (long)args[1]!;
            var existing = Load<WebProfile>("profiles", owner);
            if ((existing?.StorageRevision ?? 0) != expected) return ValueTask.FromResult((TValue)(object)false);
            values["profiles:" + owner] = (string)args[2]!;
            if (identifier == "compareExchangeProfileSnapshot") values["public:schedule"] = (string)args[3]!;
            return ValueTask.FromResult((TValue)(object)true);
        }
        return ValueTask.FromResult(default(TValue)!);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
