using Vograph.Core.Services.Communities;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

internal static class GroupMedia
{
    public const int MaxBytes = 8 * 1024 * 1024;

    public static Task<ChatMessageResponse> Place(CommunityHttpClient api, string token, Guid conversationId, string kind, string name, byte[] bytes, Guid? replyTo, CancellationToken ct, int? durationMs = null, Guid? topicId = null)
    {
        if (api is null || kind is not ("image" or "video" or "file" or "voice" or "circle") || bytes is null
            || bytes.Length < 1 || bytes.Length > (kind == "voice" ? 2 * 1024 * 1024 : MaxBytes)
            || kind is ("voice" or "circle") && durationMs is null
            || durationMs is int ms && (kind is not ("voice" or "circle") || ms < 1 || ms > (kind == "voice" ? 180_000 : 60_000)))
            throw new CommunityClientException(CommunityClientFailure.InvalidRequest);
        var clean = string.IsNullOrWhiteSpace(name) ? Label(kind) : Path.GetFileName(name.Trim());
        return api.SendMediaAsync(token, conversationId, kind, string.IsNullOrWhiteSpace(clean) ? Label(kind) : clean, bytes, replyTo, ct, durationMs, topicId);
    }

    public static string Label(string kind) => kind switch
    {
        "image" => "Фото",
        "video" => "Видео",
        "voice" => "Голосовое сообщение",
        "circle" => "Кружок",
        _ => "Документ"
    };

    public static string SafeName(string? name, string kind)
    {
        var last = (name ?? "").Replace('\\', '/').Split('/').LastOrDefault()?.Trim() ?? "";
        var clean = new string(last.Where(c => !char.IsControl(c) && c is not ('<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')).ToArray());
        if (clean is "" or "." or "..") clean = Label(kind);
        return clean.Length <= 80 ? clean : clean[..80];
    }
}
