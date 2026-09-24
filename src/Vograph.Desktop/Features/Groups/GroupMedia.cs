using Vograph.Core.Services.Communities;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

internal static class GroupMedia
{
    public const int MaxBytes = 8 * 1024 * 1024;

    public static Task<ChatMessageResponse> Place(CommunityHttpClient api, string token, Guid conversationId, string kind, string name, byte[] bytes, Guid? replyTo, CancellationToken ct)
    {
        if (api is null || bytes is null || bytes.Length is < 1 or > MaxBytes) throw new CommunityClientException(CommunityClientFailure.InvalidRequest);
        var clean = string.IsNullOrWhiteSpace(name) ? Label(kind) : Path.GetFileName(name.Trim());
        return api.SendMediaAsync(token, conversationId, kind, string.IsNullOrWhiteSpace(clean) ? Label(kind) : clean, bytes, replyTo, ct);
    }

    public static string Label(string kind) => kind switch
    {
        "image" => "Фото",
        "video" => "Видео",
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
