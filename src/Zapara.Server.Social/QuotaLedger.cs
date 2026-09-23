using System.Text.RegularExpressions;
using Npgsql;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

public sealed class QuotaLedger(AccountsDataSource data, Microsoft.Extensions.Configuration.IConfiguration configuration)
{
    private static readonly Regex SchemaName = new(@"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant);

    public async Task Reserve(IAccountUnitOfWork accounts, string token, long bytes, string? groupId, CancellationToken ct)
    {
        var user = await accounts.ExecuteAsync(token, (context, _) => Task.FromResult(context.UserId), ct);
        var group = StudyGroupScope.Choose(groupId, Groups(user));
        var state = Read(user, group);
        var decision = QuotaRules.Decide(state, bytes);
        if (!decision.Allowed) throw new SocialException(413, decision.Code!);
        Add(user, group, bytes);
    }

    public async Task Release(IAccountUnitOfWork accounts, string token, long bytes, string? groupId, CancellationToken ct)
    {
        try
        {
            var user = await accounts.ExecuteAsync(token, (context, _) => Task.FromResult(context.UserId), ct);
            Add(user, StudyGroupScope.Choose(groupId, Groups(user)), -bytes);
        }
        catch (NpgsqlException) { }
    }

    public (long UserUsed, long UserLimit, long GroupUsed, long GroupLimit) Usage(Guid userId, string? groupId)
    {
        var state = Read(userId, StudyGroupScope.Choose(groupId, Groups(userId)));
        return (state.UserUsed, state.UserLimit, state.GroupUsed, state.GroupLimit);
    }

    private IReadOnlyList<string> Groups(Guid userId)
    {
        var schema = configuration["Communities:Schema"];
        if (string.IsNullOrWhiteSpace(schema) || !SchemaName.IsMatch(schema)) return [];
        try
        {
            using var connection = Open();
            using var probe = new NpgsqlCommand("SELECT to_regclass(@name)::text", connection);
            probe.Parameters.AddWithValue("name", schema + ".memberships");
            if (probe.ExecuteScalar() is not string) return [];
            using var command = new NpgsqlCommand($"""
                SELECT COALESCE(map.group_id, mem.community_id::text)
                FROM {schema}.memberships mem
                LEFT JOIN {schema}.catalog_maps map ON map.community_id = mem.community_id
                WHERE mem.user_id = @user AND mem.status = 'active'
                ORDER BY mem.created_at, mem.community_id
                """, connection);
            command.Parameters.AddWithValue("user", userId);
            var found = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var value = reader.GetString(0);
                if (value.Length is > 0 and <= 64) found.Add(value);
            }
            return found;
        }
        catch (NpgsqlException) { return []; }
    }

    private QuotaState Read(Guid userId, string? groupId)
    {
        var settings = Settings();
        var userLimit = Limit(settings, "quota_user_bytes", QuotaRules.DefaultUserBytes);
        var groupLimit = Limit(settings, "quota_group_bytes", QuotaRules.DefaultGroupBytes);
        try
        {
            using var connection = Open();
            var schema = Ensure(connection);
            var userUsed = Used(connection, schema, "user", userId.ToString("D"));
            if (groupId is null) return new(userUsed, userLimit, 0, groupLimit, false);
            return new(userUsed, userLimit, Used(connection, schema, "group", groupId), groupLimit, true);
        }
        catch (NpgsqlException)
        {
            return new(0, userLimit, 0, groupLimit, groupId is not null);
        }
    }

    private void Add(Guid userId, string? groupId, long bytes)
    {
        try
        {
            using var connection = Open();
            Ensure(connection);
            Bump(connection, "user", userId.ToString("D"), bytes);
            if (groupId is not null) Bump(connection, "group", groupId, bytes);
        }
        catch (NpgsqlException) { }
    }

    private NpgsqlConnection Open()
    {
        var connection = data.CreateConnection();
        connection.Open();
        return connection;
    }

    private IReadOnlyDictionary<string, string> Settings()
    {
        try
        {
            using var connection = Open();
            return OperatorSettings.ReadAll(connection, OperatorSettings.Schema(configuration));
        }
        catch (NpgsqlException) { return new Dictionary<string, string>(); }
    }

    private string Ensure(NpgsqlConnection connection)
    {
        var schema = OperatorSettings.Schema(configuration);
        using (var schemaCommand = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {schema}", connection)) schemaCommand.ExecuteNonQuery();
        using var command = new NpgsqlCommand(
            $"CREATE TABLE IF NOT EXISTS {schema}.quota_counters (scope text NOT NULL, scope_id text NOT NULL, bytes bigint NOT NULL, PRIMARY KEY (scope, scope_id))",
            connection);
        command.ExecuteNonQuery();
        return schema;
    }

    private static long Used(NpgsqlConnection connection, string schema, string scope, string id)
    {
        using var command = new NpgsqlCommand($"SELECT bytes FROM {schema}.quota_counters WHERE scope=@s AND scope_id=@id", connection);
        command.Parameters.AddWithValue("s", scope);
        command.Parameters.AddWithValue("id", id);
        return command.ExecuteScalar() is long value ? value : 0;
    }

    private void Bump(NpgsqlConnection connection, string scope, string id, long bytes)
    {
        var schema = OperatorSettings.Schema(configuration);
        using var command = new NpgsqlCommand($"""
            INSERT INTO {schema}.quota_counters(scope, scope_id, bytes) VALUES(@s, @id, GREATEST(@b, 0))
            ON CONFLICT (scope, scope_id) DO UPDATE SET bytes = GREATEST({schema}.quota_counters.bytes + @b, 0)
            """, connection);
        command.Parameters.AddWithValue("s", scope);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("b", bytes);
        command.ExecuteNonQuery();
    }

    private static long Limit(IReadOnlyDictionary<string, string> settings, string key, long fallback)
        => settings.TryGetValue(key, out var raw) && long.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
}
