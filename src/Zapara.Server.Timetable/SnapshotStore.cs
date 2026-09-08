using Npgsql;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Zapara.Server.Timetable;

public sealed partial class SnapshotStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;
    private readonly string quotedSchema;
    private readonly TimeProvider clock;
    private readonly Func<NpgsqlConnection> dedicatedFactory;

    public SnapshotStore(NpgsqlDataSource dataSource, string schema, TimeProvider timeProvider,
        Func<NpgsqlConnection> dedicatedConnectionFactory)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(dedicatedConnectionFactory);
        if (schema is null || !Regex.IsMatch(schema, @"\A[a-z][a-z0-9_]{0,62}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("Недопустимая схема расписания.");
        this.dataSource = dataSource;
        this.schema = schema;
        quotedSchema = $"\"{schema}\"";
        clock = timeProvider;
        dedicatedFactory = dedicatedConnectionFactory;
    }

    private long LockKey(NpgsqlConnection connection) => BinaryPrimitives.ReadInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes($"zapara.timetable.v1\0{connection.Database}\0{schema}")));

    public async Task<RefreshLease?> TryAcquireAsync(CancellationToken ct = default)
    {
        NpgsqlConnection? connection = null;
        var transferred = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            connection = dedicatedFactory();
            var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
            var expected = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
            if (settings.Pooling || connection.State != System.Data.ConnectionState.Closed
                || settings.Host != expected.Host || settings.Port != expected.Port || settings.Database != expected.Database)
                throw new InvalidOperationException("Требуется отдельное непуловое соединение хранилища.");
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey(connection));
            if (await command.ExecuteScalarAsync(ct) is not true) return null;
            var id = Guid.NewGuid();
            await using (var transaction = await connection.BeginTransactionAsync(ct))
            {
                await using var start = new NpgsqlCommand($"""
                    UPDATE {quotedSchema}.refresh_attempts
                    SET status='abandoned', error_code='abandoned', finished_at=@now WHERE status='running';
                    INSERT INTO {quotedSchema}.refresh_attempts(attempt_id,started_at,status) VALUES(@id,@now,'running');
                    """, connection, transaction);
                start.Parameters.AddWithValue("now", clock.GetUtcNow().ToUniversalTime());
                start.Parameters.AddWithValue("id", id);
                await start.ExecuteNonQueryAsync(ct);
                await transaction.CommitAsync(ct);
            }
            transferred = true;
            return new RefreshLease(this, connection, id);
        }
        catch (Exception error) when (IsDatabaseError(error)) { throw new StoreException(FailureCode.DbUnavailable); }
        finally
        {
            if (!transferred && connection is not null) await connection.DisposeAsync();
        }
    }

    private static bool IsDatabaseError(Exception error) => error is NpgsqlException or IOException or TimeoutException;
}
