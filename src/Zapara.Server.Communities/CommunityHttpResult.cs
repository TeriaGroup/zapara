using Microsoft.AspNetCore.Http;
using System.Text.Json.Nodes;
using Zapara.Contracts.Communities;
using Zapara.Server.Accounts;

namespace Zapara.Server.Communities;

internal static class CommunityHttpResult
{
    internal static IResult Json<T>(T value, int status = 200) => new TypedJson<T>(value, status);
    internal static IResult Bytes(byte[] body) => new FrozenJson(body, 200, "application/octet-stream");
    internal static IResult Problem(int status, string code)
    {
        var title = status switch
        {
            400 or 413 or 415 => "Некорректный запрос",
            401 => "Не удалось подтвердить доступ",
            403 => "Доступ запрещён",
            404 => "Ресурс не найден",
            409 => "Конфликт изменения",
            429 => "Слишком много запросов",
            503 => "Сервис временно недоступен",
            _ => "Внутренняя ошибка сервера"
        };
        return new FrozenJson(CommunityJson.Serialize(new CommunityError(title, status, code)), status, "application/problem+json");
    }
    internal static IResult From(CommunityServiceException exception) => Problem(exception.Status, exception.Code);
    internal static IResult From(AccountServiceException exception) => Problem(exception.Failure switch
    {
        AccountFailure.InvalidCredentials or AccountFailure.InvalidSession => 401,
        AccountFailure.UsernameUnavailable => 409,
        AccountFailure.SessionNotFound or AccountFailure.ExportNotFound => 404,
        AccountFailure.InvalidRequest => 400,
        AccountFailure.RateLimited => 429,
        AccountFailure.DbUnavailable or AccountFailure.RecoveryUnavailable => 503,
        _ => 500
    }, exception.Code);

    // Version 1 readers reject control characters in Body. Presentation changes only;
    // stored content and modern/native-v2/browser responses retain the original paragraphs.
    private static object? Legacy(object? value) => value switch
    {
        HomeworkResponse h => new HomeworkResponse(h.HomeworkId, h.CommunityId, h.Title, SingleLine(h.Body), h.Revision, h.CreatedAt, h.UpdatedAt, h.DeadlineAt, h.TopicId, h.Audience, h.CanEdit, h.CanComplete),
        AnnouncementResponse a => new AnnouncementResponse(a.AnnouncementId, a.CommunityId, a.Title, SingleLine(a.Body), a.Revision, a.CreatedAt, a.UpdatedAt),
        IEnumerable<HomeworkResponse> homework => homework.Select(h => Legacy(h)).ToArray(),
        IEnumerable<AnnouncementResponse> announcements => announcements.Select(a => Legacy(a)).ToArray(),
        _ => value
    };
    // Existing Windows releases reject additional fields. New clients negotiate this
    // shape explicitly; /space has never had a legacy representation.
    private static object? LegacyGroupSpace(object? value) => value switch
    {
        GroupTopicListResponse page => new { topics = page.Topics.Where(t => t.Supported && t.Kind is "chat" or "ballots").Select(t => LegacyGroupSpace(t)).ToArray(), page.CanManageChannels },
        GroupTopicResponse t => new { t.TopicId, t.Title, t.Icon, t.LastBody, t.LastAuthor, t.LastAt, t.Unread, t.CanDelete, t.Kind,
            t.ActiveBallots, t.Description, t.Accent, t.Pinned, t.WritePolicy, canPost = t.Permissions.Count == 0 ? t.CanPost : t.Permissions.Contains("post") },
        GroupDeskResponse d => new { d.Headman, roles = d.Roles.Select(r => new { r.RoleId, r.Name }).ToArray(), d.Grants, d.Applicants, d.Powers, d.Mine },
        HomeworkResponse h => new { h.HomeworkId, h.CommunityId, h.Title, h.Body, h.Revision, h.CreatedAt, h.UpdatedAt },
        GroupHomeworkCopyResponse h => new { h.HomeworkId, h.Title, h.Body, h.Revision, h.Completed, h.CompletionRevision },
        IEnumerable<HomeworkResponse> items => items.Select(h => LegacyGroupSpace(h)).ToArray(),
        IEnumerable<GroupHomeworkCopyResponse> items => items.Select(h => LegacyGroupSpace(h)).ToArray(),
        IEnumerable<object> items => items.Select(LegacyGroupSpace).ToArray(),
        _ => value
    };
    private static string SingleLine(string body) => body.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
    private sealed class TypedJson<T>(T value, int status) : IResult
    {
        public Task ExecuteAsync(HttpContext context)
        {
            object? shown = context.Request.Path.StartsWithSegments("/api/v1/communities") ? Legacy(value) : value;
            var homework = context.Request.Headers["X-Zapara-Homework"].ToString() == "1";
            var modern = homework || context.Request.Headers["X-Zapara-Group-Space"].ToString() == "1"
                || (context.Request.Path.Value?.Split('/').Contains("space", StringComparer.Ordinal) ?? false);
            if (!modern) shown = LegacyGroupSpace(shown);
            else context.Response.Headers["X-Zapara-Group-Space"] = "1";
            var bytes = CommunityJson.Serialize(shown);
            if (homework) context.Response.Headers["X-Zapara-Homework"] = "1";
            else
            {
                // Native clients reject unknown fields. Preserve both earlier negotiated shapes.
                var legacy = JsonNode.Parse(bytes);
                RemoveHomeworkMetadata(legacy);
                bytes = CommunityJson.Serialize(legacy);
            }
            return new FrozenJson(bytes, status, "application/json; charset=utf-8").ExecuteAsync(context);
        }
    }
    private static void RemoveHomeworkMetadata(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("homeworkId") && obj.ContainsKey("title"))
            { obj.Remove("audience"); obj.Remove("canEdit"); obj.Remove("canComplete"); }
            if (obj.ContainsKey("maxRoles")) obj.Remove("homeworkAudience");
            foreach (var child in obj.ToArray()) RemoveHomeworkMetadata(child.Value);
        }
        else if (node is JsonArray list) foreach (var item in list) RemoveHomeworkMetadata(item);
    }
    private sealed class FrozenJson(byte[] body, int status, string contentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.StatusCode = status;
            context.Response.ContentType = contentType;
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }
}
