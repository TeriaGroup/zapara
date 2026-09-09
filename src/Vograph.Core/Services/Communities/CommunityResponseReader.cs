using System.Text.Json;
using Zapara.Contracts.Communities;

namespace Vograph.Core.Services.Communities;

internal static class CommunityResponseReader
{
    internal static T Read<T>(byte[] bytes)
    {
        var result = CommunityJson.Parse<T>(bytes);
        if (result is Array array)
            foreach (var item in array)
                if (item is null) throw Invalid();
        if (result is PollResultsResponse)
        {
            using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            Names(json.RootElement, "pollId", "totalVotes", "options");
            foreach (var option in json.RootElement.GetProperty("options").EnumerateArray())
                Names(option, "optionId", "label", "votes");
        }
        return result;
    }

    private static void Names(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name) || Array.IndexOf(allowed, property.Name) < 0)
                throw Invalid();
        }
        if (names.Count != allowed.Length) throw Invalid();
    }

    private static ArgumentException Invalid() => new("Недопустимый контракт сообщества.");
}
