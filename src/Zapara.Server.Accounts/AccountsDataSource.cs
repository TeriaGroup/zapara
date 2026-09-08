using Npgsql;

namespace Zapara.Server.Accounts;

public sealed class AccountsDataSource : IDisposable, IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string? migrationConnectionString;
    internal AccountsDataSource(string connectionString, string? migrationConnectionString)
    {
        dataSource = NpgsqlDataSource.Create(connectionString);
        this.migrationConnectionString = migrationConnectionString;
    }
    public NpgsqlConnection CreateConnection() => dataSource.CreateConnection();
    public NpgsqlConnection CreateMigrationConnection()
    {
        if (migrationConnectionString is null) throw new InvalidOperationException("Не задано подключение для миграции Accounts.");
        return new(new NpgsqlConnectionStringBuilder(migrationConnectionString) { Pooling = false }.ConnectionString);
    }
    public void Dispose() => dataSource.Dispose();
    public ValueTask DisposeAsync() => dataSource.DisposeAsync();
    public override string ToString() => "AccountsDataSource { [REDACTED] }";
}
