using Microsoft.Data.Sqlite;

namespace Vograph.Core.Services.Sync;

internal static class PrivateSyncSchema
{
    public static void Ensure(SqliteConnection conn)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS sync_outbox (
                    opId TEXT PRIMARY KEY,
                    entityType TEXT NOT NULL,
                    entityId TEXT NOT NULL,
                    expectedRevision INTEGER NOT NULL,
                    action TEXT NOT NULL,
                    payload BLOB,
                    localRowId INTEGER,
                    status TEXT NOT NULL,
                    createdAtUtc TEXT NOT NULL,
                    syncEpoch TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_sync_outbox_entity ON sync_outbox(entityType, entityId, status);
                CREATE TABLE IF NOT EXISTS sync_state (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    syncEpoch TEXT,
                    afterSequence INTEGER NOT NULL DEFAULT 0
                );
                INSERT OR IGNORE INTO sync_state (id, afterSequence) VALUES (1, 0);
                CREATE TABLE IF NOT EXISTS sync_alias (
                    entityType TEXT NOT NULL,
                    localId INTEGER NOT NULL,
                    entityUuid TEXT NOT NULL,
                    PRIMARY KEY (entityType, localId)
                );
                CREATE UNIQUE INDEX IF NOT EXISTS idx_sync_alias_uuid ON sync_alias(entityType, entityUuid);
                CREATE TABLE IF NOT EXISTS sync_draft (
                    entityType TEXT NOT NULL,
                    entityId TEXT NOT NULL,
                    opId TEXT,
                    localPayload TEXT,
                    serverPayload TEXT,
                    createdAtUtc TEXT NOT NULL,
                    PRIMARY KEY (entityType, entityId)
                );
                CREATE TABLE IF NOT EXISTS homework_completion (
                    entityUuid TEXT PRIMARY KEY,
                    done INTEGER NOT NULL DEFAULT 0,
                    doneAtUtc TEXT,
                    revision INTEGER NOT NULL DEFAULT 0,
                    tombstone INTEGER NOT NULL DEFAULT 0
                );
                """;
            cmd.ExecuteNonQuery();
        }

        AddColumn(conn, "homework", "entityUuid", "TEXT");
        AddColumn(conn, "homework", "revision", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(conn, "homework", "tombstone", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(conn, "homework", "createdAtUtc", "TEXT");
        AddColumn(conn, "homework", "legacyCreatedLocalDate", "TEXT");
        AddColumn(conn, "overrides", "entityUuid", "TEXT");
        AddColumn(conn, "overrides", "revision", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(conn, "overrides", "tombstone", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(conn, "overrides", "createdAtUtc", "TEXT");
        AddColumn(conn, "friends", "entityUuid", "TEXT");
        AddColumn(conn, "friends", "revision", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(conn, "friends", "tombstone", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(conn, "settings", "entityUuid", "TEXT");
        AddColumn(conn, "settings", "revision", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(conn, "settings", "tombstone", "INTEGER NOT NULL DEFAULT 0");
        using (var idx = conn.CreateCommand())
        {
            idx.CommandText = """
                CREATE UNIQUE INDEX IF NOT EXISTS idx_homework_entity ON homework(entityUuid) WHERE entityUuid IS NOT NULL;
                CREATE UNIQUE INDEX IF NOT EXISTS idx_overrides_entity ON overrides(entityUuid) WHERE entityUuid IS NOT NULL;
                CREATE UNIQUE INDEX IF NOT EXISTS idx_friends_entity ON friends(entityUuid) WHERE entityUuid IS NOT NULL;
                """;
            idx.ExecuteNonQuery();
        }
    }

    private static void AddColumn(SqliteConnection conn, string table, string column, string definition)
    {
        if (HasColumn(conn, table, column)) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        cmd.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
