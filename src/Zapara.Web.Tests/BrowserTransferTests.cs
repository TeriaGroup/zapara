using System.Text;
using System.Text.Json;
using Xunit;
using Zapara.Contracts.Sync;
using Zapara.Web.Services;

namespace Zapara.Web.Tests;

public sealed class BrowserTransferTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LegacyLocalCreationKeepsItsCalendarDateAndIdenticalRowsRemainDistinct()
    {
        var text = """
            {"Version":1,"ExportedAt":"2026-09-21T00:00:00Z","Overrides":[],"Homework":[
             {"SubjectRawNormalized":"лек высш. мат.","Text":"Задачи","CreatedAt":"2026-09-20T23:45:00","TargetNthOccurrence":2,"Status":"done","DoneAt":"2026-09-21T00:10:00","DueDateComputed":null},
             {"SubjectRawNormalized":"лек высш. мат.","Text":"Задачи","CreatedAt":"2026-09-20T23:45:00","TargetNthOccurrence":2,"Status":"done","DoneAt":"2026-09-21T00:10:00","DueDateComputed":null}],
             "Friends":[],"Settings":{"MyGroupId":"О3313","NotifyTime1":"20:00","NotifyTime2":null,"Language":"ru"}}
            """;
        var source = LegacyTransferCodec.Parse(Encoding.UTF8.GetBytes(text), Now);
        var homework = source.Records.Where(row => row.EntityType == "homework").ToArray();
        Assert.Equal(2, homework.Length);
        Assert.NotEqual(homework[0].EntityId, homework[1].EntityId);
        Assert.Equal(new DateOnly(2026, 9, 20), Assert.IsType<HomeworkValue>(homework[0].Value).LegacyCreatedLocalDate);
        Assert.Equal("лек высш. мат.", Assert.IsType<HomeworkValue>(homework[0].Value).SubjectKey);
        Assert.Equal(homework.Select(row => row.EntityId), source.Records.Where(row => row.EntityType == "completion").Select(row => row.EntityId));
        var second = LegacyTransferCodec.Parse(Encoding.UTF8.GetBytes(text), Now.AddHours(3));
        Assert.Equal(homework.Select(row => row.SourceKey), second.Records.Where(row => row.EntityType == "homework").Select(row => row.SourceKey));
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Version\":1,\"ExportedAt\":\"2026-09-21T00:00:00Z\"}")]
    [InlineData("{\"Version\":2,\"ExportedAt\":\"2026-09-21T00:00:00Z\"}")]
    [InlineData("{\"Version\":1,\"ExportedAt\":\"bad-date\"}")]
    public void MalformedOrAmbiguousPayloadIsRejectedBeforeAnyStateChange(string json)
        => Assert.Throws<InvalidDataException>(() => LegacyTransferCodec.Parse(Encoding.UTF8.GetBytes(json), Now));

    [Fact]
    public void OversizeAndDeeplyNestedFilesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => LegacyTransferCodec.Parse(new byte[LegacyTransferCodec.MaximumBytes + 1], Now));
        var nested = string.Concat(Enumerable.Repeat("{\"a\":", 40)) + "null" + new string('}', 40);
        Assert.Throws<InvalidDataException>(() => LegacyTransferCodec.Parse(Encoding.UTF8.GetBytes(nested), Now));
    }

    [Fact]
    public void OffsetCreationKeepsTheOriginalCalendarDayAcrossMidnight()
    {
        var payload = new LegacyTransferPayload { ExportedAt = "2026-09-21T00:00:00Z", Homework = [new()
        { SubjectRawNormalized = "Математика", Text = "Задача", CreatedAt = "2026-09-21T01:30:00+03:00", TargetNthOccurrence = 2 }] };
        var value = LegacyTransferCodec.Parse(JsonSerializer.SerializeToUtf8Bytes(payload), Now).Records.Single(row => row.EntityType == "homework").Value;
        var homework = Assert.IsType<HomeworkValue>(value);
        Assert.Equal(new DateOnly(2026, 9, 21), homework.LegacyCreatedLocalDate);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 22, 30, 0, TimeSpan.Zero), homework.CreatedAtUtc);
    }
}
