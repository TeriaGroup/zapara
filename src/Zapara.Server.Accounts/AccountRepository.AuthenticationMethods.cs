namespace Zapara.Server.Accounts;

internal sealed partial class AccountRepository
{
    internal async Task<IReadOnlyList<string>> AuthenticationMethodsAsync(Guid userId)
    {
        var methods = new List<string>();
        if (await CredentialAsync(userId) is not null) methods.Add("password");
        await using var version = Command($"SELECT max(version) FROM {schema}.schema_migrations");
        if ((int)(await version.ExecuteScalarAsync(ct))! >= 2)
        {
            await using var command = Command($"SELECT provider FROM {schema}.external_identities WHERE user_id=@p0 ORDER BY provider", userId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) methods.Add(reader.GetString(0));
        }
        return methods.AsReadOnly();
    }
}
