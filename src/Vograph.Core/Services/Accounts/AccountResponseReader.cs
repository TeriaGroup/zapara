using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Zapara.Contracts.Accounts;

namespace Vograph.Core.Services.Accounts;

internal static class AccountResponseReader
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = AccountJson.CreateOptions();
        options.MaxDepth = 32;
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind == JsonTypeInfoKind.Object)
                foreach (var property in info.Properties) property.IsRequired = true;
        });
        options.TypeInfoResolver = resolver;
        return options;
    }

    internal static T Read<T>(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
        NoDuplicates(json.RootElement);
        var result = json.RootElement.Deserialize<T>(Options) ?? throw new JsonException();
        switch (result)
        {
            case SessionResponse session: ValidateSession(session); break;
            case MeResponse me:
                if (me.User is null || me.FamilyId == Guid.Empty || me.AuthenticationMethods is null
                    || me.AuthenticationMethods.Count is < 1 or > 3 || me.AuthenticationMethods.Distinct().Count() != me.AuthenticationMethods.Count
                    || me.AuthenticationMethods.Any(m => m is not ("password" or "vk" or "yandex"))) throw new JsonException();
                break;
            case DevicesResponse page:
                if (page.Devices is null || page.Devices.Count > 100 || !ValidCursor(page.NextCursor)
                    || page.Devices.Select(d => d?.FamilyId).Distinct().Count() != page.Devices.Count) throw new JsonException();
                foreach (var device in page.Devices)
                {
                    if (device is null) throw new JsonException();
                    AccountValidation.Id(device.FamilyId);
                    AccountValidation.Id(device.DeviceId);
                    AccountValidation.DeviceName(device.DeviceName);
                    AccountValidation.Platform(device.Platform);
                    AccountValidation.Utc(device.CreatedAt);
                    AccountValidation.Utc(device.LastSeenAt);
                    AccountValidation.Utc(device.ExpiresAt);
                    if (device.LastSeenAt < device.CreatedAt || device.ExpiresAt <= device.CreatedAt) throw new JsonException();
                }
                break;
        }
        return result;
    }

    internal static void ValidateSession(SessionResponse session)
    {
        if (session.User.CreatedAt >= session.AccessExpiresAt || session.User.CreatedAt >= session.RefreshExpiresAt)
            throw new AccountClientException(AccountClientFailure.InvalidPayload);
    }

    internal static bool ValidCursor(string? cursor) => cursor is null || (cursor.Length == 55
        && cursor.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'));

    internal static void NoDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException();
                NoDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) NoDuplicates(item);
    }
}
