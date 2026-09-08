using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Zapara.Contracts.Sync;

namespace Zapara.Server.Tests;

public sealed class SyncContractTests
{
    private static readonly Guid Epoch = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Op = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Entity = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static HomeworkValue Homework(string text = "  Тест 🌍\n ") => new("  ЛК. ЁЖ\t  ТЕСТ ", "лк. еж тест", text, 10, Now, new(2026, 9, 7));
    private static SyncMutation Mutation(SyncValue? value = null) => new(Epoch, Op, "homework", Entity, 0, "upsert", value ?? Homework());

    [Fact]
    public void Digest_is_typed_order_independent_epoch_bound_and_preserves_text()
    {
        var original = Mutation();
        var json = JsonNode.Parse(SyncJson.Serialize(original))!.AsObject();
        var reversed = new JsonObject(json.Reverse().Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
        var parsed = SyncJson.Parse<SyncMutation>(Encoding.UTF8.GetBytes(reversed.ToJsonString()));
        Assert.Equal(original, parsed);
        Assert.Equal(SyncJson.Digest(original), SyncJson.Digest(parsed));
        Assert.Equal(32, SyncJson.Digest(original).Length);
        Assert.Equal("zapara.sync.mutation.v1", SyncJson.DigestVersion);
        Assert.NotEqual(SyncJson.Digest(original), SyncJson.Digest(new(Guid.NewGuid(), Op, "homework", Entity, 0, "upsert", Homework())));
        Assert.NotEqual(SyncJson.Digest(original), SyncJson.Digest(Mutation(Homework("Тест 🌍"))));
        Assert.Equal("  Тест 🌍\n ", Assert.IsType<HomeworkValue>(parsed.Value).Text);
        Assert.Equal("лк. еж тест", SyncValidation.NormalizeSubject("  ЛК. ЁЖ\t  ТЕСТ "));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("valueUnknown")]
    [InlineData("valueMissing")]
    [InlineData("offset")]
    [InlineData("date")]
    [InlineData("nul")]
    [InlineData("surrogate")]
    [InlineData("negative")]
    [InlineData("key")]
    public void Strict_wire_rejects_invalid_shape_and_data(string kind)
    {
        var node = JsonNode.Parse(SyncJson.Serialize(Mutation()))!.AsObject();
        var value = node["value"]!.AsObject();
        switch (kind)
        {
            case "unknown": node["userId"] = Entity.ToString(); break;
            case "missing": node.Remove("expectedRevision"); break;
            case "valueUnknown": value["dueAt"] = "2026-09-09"; break;
            case "valueMissing": value.Remove("legacyCreatedLocalDate"); break;
            case "offset": value["createdAtUtc"] = "2026-09-08T15:00:00+03:00"; break;
            case "date": value["legacyCreatedLocalDate"] = "2026-02-30"; break;
            case "nul": value["text"] = "bad\0text"; break;
            case "negative": node["expectedRevision"] = -1; break;
            case "key": value["subjectKey"] = "еж тест"; break;
        }
        var json = node.ToJsonString();
        if (kind == "duplicate") json = json.Replace("\"expectedRevision\":0", "\"expectedRevision\":0,\"expectedRevision\":0");
        if (kind == "surrogate") json = json.Replace("\"text\":", "\"text\":\"\\uD800\",\"discard\":");
        Assert.Throws<ArgumentException>(() => SyncJson.Parse<SyncMutation>(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Limits_singleton_deletion_and_typed_matching()
    {
        Assert.Equal(8000, Homework(string.Concat(Enumerable.Repeat("🌍", 4000))).Text.Length);
        Assert.Throws<ArgumentException>(() => Homework(new string('x', 4001)));
        Assert.Throws<ArgumentException>(() => Homework("\ud800"));
        Assert.Throws<ArgumentException>(() => SyncJson.Parse<SyncMutation>(new byte[SyncValidation.RequestBytes + 1]));
        Assert.Throws<ArgumentException>(() => SyncJson.Parse<SyncMutation>(new byte[] { 0xff, 0xfe }));
        var settings = new SettingsValue(null, false, "09:00", null, 100, true);
        Assert.Throws<ArgumentException>(() => new SyncMutation(Epoch, Op, "settings", Entity, 0, "upsert", settings));
        var singleton = new SyncMutation(Epoch, Op, "settings", SyncValidation.SettingsId, 0, "upsert", settings);
        Assert.Equal(singleton, SyncJson.Parse<SyncMutation>(SyncJson.Serialize(singleton)));
        Assert.Throws<ArgumentException>(() => new SyncMutation(Epoch, Op, "homework", Entity, 0, "delete", null));
        Assert.Throws<ArgumentException>(() => new SyncMutation(Epoch, Op, "homework", Entity, 1, "delete", Homework()));
        Assert.Throws<ArgumentException>(() => new SyncMutation(Epoch, Op, "friend", Entity, 0, "upsert", Homework()));
        Assert.Throws<ArgumentException>(() => new SettingsValue(null, false, "9:00", null, 0, false));
        Assert.Throws<ArgumentException>(() => new FriendValue(null, "group", "names", 6, true));
        Assert.Throws<ArgumentException>(() => new OverrideValue("A", "a", "weekday:8", "A", null, Now));
        Assert.True(SyncJson.Serialize(Mutation(Homework(new string('\u0001', 4000)))).Length < SyncValidation.RequestBytes);
    }

    [Fact]
    public void All_values_records_outcomes_and_immutable_pages_roundtrip()
    {
        SyncValue[] values = [Homework(), new CompletionValue(true, Now), new OverrideValue("A", "a", "weekday:1", "  Имя ", null, Now),
            new FriendValue(null, "ИВТ", "Имя", 1, true), new SettingsValue(null, false, null, "23:59", 0, false)];
        string[] types = ["homework", "completion", "override", "friend", "settings"];
        for (var i = 0; i < values.Length; i++)
        {
            var record = new SyncRecord(types[i], i == 4 ? SyncValidation.SettingsId : Entity, 1, false, Now, values[i]);
            Assert.Equal(record, SyncJson.Parse<SyncRecord>(SyncJson.Serialize(record)));
        }
        var tombstone = new SyncRecord("homework", Entity, 1, true, Now, null);
        var metadata = new SyncMetadata(Epoch, 1, 0);
        foreach (var result in new[] { new SyncMutationResult(200, "applied", metadata, tombstone),
            new SyncMutationResult(409, "revision_conflict", metadata, tombstone), new SyncMutationResult(409, "revision_conflict", metadata, null),
            new SyncMutationResult(409, "op_id_reused", metadata, null), new SyncMutationResult(410, "sync_reset", metadata, null) })
            Assert.Equal(result, SyncJson.Parse<SyncMutationResult>(SyncJson.Serialize(result)));
        var changes = new List<SyncChange> { new(1, Op, tombstone) };
        var page = new SyncChangesPage(metadata, 0, 1, false, changes);
        changes.Clear();
        Assert.Single(page.Changes);
        Assert.Single(SyncJson.Parse<SyncChangesPage>(SyncJson.Serialize(page)).Changes);
        Assert.Throws<ArgumentException>(() => new SyncChangesPage(metadata, 0, 1, false, []));
        var manifest = new SyncResyncManifest(Entity, Epoch, 1, Now, Now.AddMinutes(10), 1);
        var resync = new SyncResyncPage(manifest, 0, 1, false, [new(1, tombstone)]);
        Assert.Single(SyncJson.Parse<SyncResyncPage>(SyncJson.Serialize(resync)).Items);
        Assert.Throws<ArgumentException>(() => new SyncResyncPage(manifest, 0, 2, false, [new(1, tombstone)]));
        Assert.Throws<ArgumentException>(() => new SyncMutationResult(200, "revision_conflict", metadata, tombstone));
    }
}
