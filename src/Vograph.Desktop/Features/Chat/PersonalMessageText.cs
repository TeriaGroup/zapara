using System.Text.RegularExpressions;
using Zapara.Contracts.Social;

namespace Vograph.Desktop.Features.Chat;

internal static class PersonalMessageText
{
    public static string? CopyText(SocialMessageResponse message)
    {
        if (message.Deleted || message.Kind is not ("text" or "image" or "file" or "video")) return null;
        var body = message.Body?.Trim();
        if (string.IsNullOrEmpty(body)) return null;
        if (message.Kind != "text" && Regex.IsMatch(body, @"^(?:https?://[^/]+)?/?(?:web-api|api/v1)/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return null;
        return body;
    }

    public static string SearchText(SocialMessageResponse message) => message.Deleted ? ""
        : string.Join(' ', new[] { CopyText(message), message.FileName }.Where(text => !string.IsNullOrWhiteSpace(text)));
}
