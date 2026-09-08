using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Vograph.Desktop.Tests;

public partial class TimetableApiClientTests
{
    private const string Pin = "11111111-1111-4111-8111-111111111111";
    private const string NewPin = "22222222-2222-4222-8222-222222222222";
    private static JsonObject Group(string id, int count = 1) => new()
    {
        ["id"] = id, ["name"] = "ТЕСТ-ГРУППА", ["lessonCount"] = count
    };

    private static JsonObject Envelope(string pin = Pin) => JsonNode.Parse($$$"""
        {"period":{"start":"2026-09-01","weekCount":2,"title":"Тестовый семестр","timeZone":"Europe/Moscow"},
         "meta":{"snapshotId":"{{{pin}}}","fetchedAt":"2026-09-08T10:00:00+00:00",
         "publishedAt":"2026-09-08T10:01:00Z","sourceModifiedAt":null,"sourceKind":"file",
         "sourceUrl":null,"sourceSha256":"{{{new string('a', 64)}}}","stale":false},
         "refresh":{"lastAttemptId":null,"lastAttemptStatus":null,"lastSuccessAt":null,
         "lastFailureAt":null,"lastFailureCode":null,"abandoned":false}}
        """)!.AsObject();

    private static JsonObject Catalog(string pin = Pin, params string[] ids)
    {
        var root = Envelope(pin);
        root["groups"] = new JsonArray((ids.Length == 0 ? new[] { "a", "empty" } : ids)
            .Select(id => (JsonNode)Group(id, id == "empty" ? 0 : 1)).ToArray());
        return root;
    }

    private static JsonObject Schedule(string id = "a", string pin = Pin)
    {
        var root = Envelope(pin);
        root["group"] = Group(id, id == "empty" ? 0 : 1);
        root["lessons"] = id == "empty" ? new JsonArray() : JsonNode.Parse("""
            [{"dayOfWeek":1,"parity":0,"index":1,"timeStart":"09:00","timeEnd":"10:35",
              "subjectRaw":"ЛЕК Ёлка  Тест","subjectNormalized":"лек елка тест","typeRaw":"лек",
              "teacherRaw":"Тестовый преподаватель","classroomRaw":"493*","roomRaw":"493","buildingRaw":"*"}]
            """);
        return root;
    }

    private static HttpResponseMessage Json(JsonNode root) => Text(root.ToJsonString());
    private static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
    private static HttpResponseMessage MissingPin() => Text(
        "{\"title\":\"Снимок не найден\",\"status\":404,\"code\":\"snapshot_not_found\"}", HttpStatusCode.NotFound);

    private sealed class TransportHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => respond(request, ct);
    }
}
