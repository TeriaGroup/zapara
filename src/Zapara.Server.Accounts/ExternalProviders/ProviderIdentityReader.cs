using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Zapara.Server.Accounts.ExternalProviders;

internal static class ProviderIdentityReader
{
    internal static string RequiredString(JsonElement json, string property, int max = 8192)
    {
        if (!json.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
        return ProviderValidation.Printable(value.GetString(), max, ExternalProviderFailure.InvalidResponse);
    }

    internal static string VkSubject(JsonElement value)
    {
        // Official examples leave the scalar type open; preserve decimal JSON lexemes without Int64 coercion.
        var subject = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        if (value.ValueKind == JsonValueKind.Number && (subject is null || subject.Any(c => c is < '0' or > '9')))
            throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
        return ProviderValidation.Printable(subject, 256, ExternalProviderFailure.InvalidResponse);
    }

    internal static string? DisplayName(JsonElement user, bool vk)
    {
        var raw = vk ? OptionalName(user, "first_name") + " " + OptionalName(user, "last_name") : OptionalName(user, "display_name");
        if (raw is null) return null;
        var output = new StringBuilder();
        var remaining = raw.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty && count < 80)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            remaining = remaining[Math.Max(consumed, 1)..];
            if (status != OperationStatus.Done || rune.Value is '<' or '>' or '&' ||
                Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or
                UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                continue;
            output.Append(rune.ToString());
            count++;
        }
        var result = output.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string? OptionalName(JsonElement user, string property)
    {
        if (!user.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
        // Invalid escaped surrogate data in optional profile text is dropped, never used as authority.
        try { return value.GetString(); }
        catch (InvalidOperationException) { return null; }
    }
}
