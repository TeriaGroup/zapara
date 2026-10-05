using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Server.Accounts;
using Zapara.Server.Admin;
using Zapara.Server.Communities;

namespace Zapara.Server.Admin.UiTests;

internal sealed class AdminUiPostgres : IAsyncDisposable
{
    internal const string Password = "correct ui password 12";
    internal string Hex { get; }
    internal string AdminSchema { get; }
    internal string AccountsSchema { get; }
    internal string CommunitiesSchema { get; }
    internal string QuotedAdmin => Quote(AdminSchema);
    internal string QuotedAccounts => Quote(AccountsSchema);
    internal string QuotedCommunities => Quote(CommunitiesSchema);
    internal AccountsConfiguration Accounts { get; }
    internal CommunitiesConfiguration Communities { get; }
    internal AdminConfiguration Admin { get; }
    internal AccountsDataSource DataSource { get; }
    internal AccountService AccountService { get; }
    internal CommunityService CommunityService { get; }
    internal Dictionary<string, string?> HostSettings { get; }

    private bool accountsCreated, communitiesCreated, adminCreated;

    private AdminUiPostgres(string hex)
    {
        Hex = hex;
        AdminSchema = "adm_ui_" + hex;
        AccountsSchema = "acc_ui_" + hex;
        CommunitiesSchema = "com_ui_" + hex;
        var raw = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES");
        ValidateConnection(raw);
        var values = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true",
            ["Accounts:Schema"] = AccountsSchema,
            ["Communities:Enabled"] = "true",
            ["Communities:Schema"] = CommunitiesSchema,
            ["Admin:Enabled"] = "true",
            ["Admin:Schema"] = AdminSchema,
            ["ConnectionStrings:Accounts"] = raw
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        Accounts = AccountsConfiguration.FromConfiguration(configuration, true);
        Communities = CommunitiesConfiguration.FromConfiguration(configuration, true);
        Admin = AdminConfiguration.FromConfiguration(configuration, true);
        DataSource = Accounts.CreateDataSource();
        AccountService = new AccountService(DataSource, Accounts, TimeProvider.System);
        CommunityService = new CommunityService(AccountService, Communities);
        HostSettings = values;
    }

    internal static async Task<AdminUiPostgres> CreateAsync()
    {
        var db = new AdminUiPostgres(Guid.NewGuid().ToString("N"));
        try
        {
            await db.ExecuteAsync($"CREATE SCHEMA {db.QuotedAccounts}");
            db.accountsCreated = true;
            Console.WriteLine($"CREATE {db.AccountsSchema}");
            await new AccountsMigrations(db.DataSource, db.Accounts).EnsureAsync(Ct, 2);
            await db.ExecuteAsync($"CREATE SCHEMA {db.QuotedCommunities}");
            db.communitiesCreated = true;
            Console.WriteLine($"CREATE {db.CommunitiesSchema}");
            await new CommunitiesMigrations(db.DataSource, db.Communities).EnsureAsync(Ct);
            await db.ExecuteAsync($"CREATE SCHEMA {db.QuotedAdmin}");
            db.adminCreated = true;
            Console.WriteLine($"CREATE {db.AdminSchema}");
            await new AdminMigrations(db.DataSource, db.Admin).EnsureAsync(Ct);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    internal Task<UserResponse> RegisterAsync(string username)
        => AccountService.RegisterAsync(new RegisterRequest(username, Password), Ct);

    internal Task<SessionResponse> LoginAccountAsync(string username)
        => AccountService.LoginAsync(new LoginRequest(username, Password, new DeviceInput(Guid.NewGuid(), "Тест", "windows")), Ct);

    internal async Task<Guid> CommunityIdAsync(string name)
        => await ScalarAsync<Guid>($"SELECT community_id FROM {QuotedCommunities}.communities WHERE name=@p0 LIMIT 1", ("p0", name));

    internal async Task RevokeStaffAsync(Guid communityId, Guid userId)
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        await using (var lockCommunity = new NpgsqlCommand(
                         $"SELECT community_id FROM {QuotedCommunities}.communities WHERE community_id=@id FOR UPDATE",
                         connection, tx))
        {
            lockCommunity.Parameters.AddWithValue("id", communityId);
            await lockCommunity.ExecuteScalarAsync(Ct);
        }
        await using (var membership = new NpgsqlCommand($"""
            UPDATE {QuotedCommunities}.memberships SET role='member'
            WHERE community_id=@c AND user_id=@u AND status='active'
            """, connection, tx))
        {
            membership.Parameters.AddWithValue("c", communityId);
            membership.Parameters.AddWithValue("u", userId);
            await membership.ExecuteNonQueryAsync(Ct);
        }
        await using (var staff = new NpgsqlCommand($"""
            UPDATE {QuotedCommunities}.staff_assignments SET revoked_at=CURRENT_TIMESTAMP
            WHERE community_id=@c AND user_id=@u AND revoked_at IS NULL
            """, connection, tx))
        {
            staff.Parameters.AddWithValue("c", communityId);
            staff.Parameters.AddWithValue("u", userId);
            await staff.ExecuteNonQueryAsync(Ct);
        }
        await tx.CommitAsync(Ct);
    }

    internal async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(Ct);
    }

    internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    internal Task<T> ScalarAsync<T>(string sql, string name, object value) => ScalarAsync<T>(sql, (name, value));

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (adminCreated) await DropAsync(AdminSchema);
            if (communitiesCreated) await DropAsync(CommunitiesSchema);
            if (accountsCreated) await DropAsync(AccountsSchema);
        }
        finally { await DataSource.DisposeAsync(); }
    }

    private async Task DropAsync(string schema)
    {
        await ExecuteAsync($"DROP SCHEMA IF EXISTS {Quote(schema)} CASCADE");
        var count = await ScalarAsync<long>("SELECT count(*) FROM pg_namespace WHERE nspname=@p0", "p0", schema);
        Console.WriteLine($"TEARDOWN {schema} remaining={count}");
        Assert.Equal(0L, count);
    }

    private static void ValidateConnection(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidOperationException("ZAPARA_TEST_POSTGRES required; no skips.");
        NpgsqlConnectionStringBuilder builder;
        try { builder = new(raw); }
        catch (ArgumentException) { throw new InvalidOperationException("Unsafe fixture connection."); }
        if (builder.Database != "zapara_test" || builder.Host is not ("localhost" or "127.0.0.1") || builder.Port != 56432)
            throw new InvalidOperationException("Only approved local fixture database is allowed.");
    }

    private static string Quote(string schema) => $"\"{schema}\"";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
