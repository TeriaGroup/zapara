using Npgsql;
using Vograph.Timetable;

namespace Zapara.Server.Timetable;

public sealed partial class SnapshotStore
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
            command.Parameters.AddWithValue("key", LockKey(connection));
            await command.ExecuteNonQueryAsync(ct);
            command.Parameters.Clear();
            command.Parameters.AddWithValue("schema", schema);
            command.CommandText = "SELECT EXISTS(SELECT FROM pg_namespace WHERE nspname=@schema)";
            if (await command.ExecuteScalarAsync(ct) is not true) throw SchemaRejected();
            command.CommandText = """
                SELECT EXISTS(SELECT FROM pg_class WHERE relnamespace=@schema::regnamespace)
                    OR EXISTS(SELECT FROM pg_proc WHERE pronamespace=@schema::regnamespace)
                    OR EXISTS(SELECT FROM pg_type WHERE typnamespace=@schema::regnamespace)
                """;
            if (await command.ExecuteScalarAsync(ct) is false)
            {
                using var stream = typeof(SnapshotStore).Assembly.GetManifestResourceStream(
                    "Zapara.Server.Timetable.Sql.001_timetable.sql")!;
                using var reader = new StreamReader(stream);
                command.CommandText = (await reader.ReadToEndAsync(ct)).Replace("{{schema}}", quotedSchema)
                    .Replace("{{url}}", "'" + TimetableParser.DefaultUrl.Replace("'", "''") + "'");
                command.Parameters.Clear();
                await command.ExecuteNonQueryAsync(ct);
            }
            await VerifySchemaAsync(connection, transaction, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception error) when (IsDatabaseError(error)) { throw SchemaRejected(); }
    }

    private static StoreException SchemaRejected() => new(FailureCode.DbUnavailable);

    private async Task VerifySchemaAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        // Exact column/identity shape and named validated constraints prevent silently resetting a partial/foreign baseline.
        await using var command = new NpgsqlCommand("""
            SELECT c.relname || ':' || a.attname || ':' || format_type(a.atttypid,a.atttypmod)
                || ':' || a.attnotnull::text || ':' || a.attidentity::text
            FROM pg_class c JOIN pg_attribute a ON a.attrelid=c.oid
            WHERE c.relnamespace=@schema::regnamespace AND c.relkind='r' AND a.attnum>0 AND NOT a.attisdropped
            ORDER BY c.relname,a.attnum
            """, connection, transaction);
        command.Parameters.AddWithValue("schema", schema);
        var actual = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) actual.Add(reader.GetString(0));
        if (!actual.SequenceEqual(ExpectedColumns)) throw SchemaRejected();
        command.CommandText = """
            SELECT c.relname || ':' || con.conname || ':' || con.contype::text || ':' || con.convalidated::text
            FROM pg_constraint con JOIN pg_class c ON c.oid=con.conrelid
            WHERE c.relnamespace=@schema::regnamespace ORDER BY c.relname,con.conname
            """;
        actual.Clear();
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) actual.Add(reader.GetString(0));
        if (!actual.SequenceEqual(ExpectedConstraints.Order(StringComparer.Ordinal))) throw SchemaRejected();
        command.CommandText = """
            SELECT conname,pg_get_constraintdef(oid) FROM pg_constraint
            WHERE connamespace=@schema::regnamespace AND contype IN ('c','p','u')
            """;
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                if (!ExpectedDefinitions.TryGetValue(reader.GetString(0), out var definition) || reader.GetString(1) != definition)
                    throw SchemaRejected();
        command.CommandText = $"""
            SELECT (SELECT count(*)=1 AND min(version)=1 FROM {quotedSchema}.schema_version)
                AND (SELECT count(*)=1 AND bool_and(singleton) FROM {quotedSchema}.state)
                AND (SELECT count(*)=2 FROM pg_constraint WHERE connamespace=@schema::regnamespace AND contype='f'
                    AND ((conrelid='{quotedSchema}.snapshots'::regclass AND confrelid='{quotedSchema}.refresh_attempts'::regclass)
                      OR (conrelid='{quotedSchema}.state'::regclass AND confrelid='{quotedSchema}.snapshots'::regclass))
                    AND conkey=ARRAY[2]::smallint[] AND confkey=ARRAY[1]::smallint[]
                    AND confdeltype='a' AND confupdtype='a' AND NOT condeferrable)
            """;
        if (await command.ExecuteScalarAsync(ct) is not true) throw SchemaRejected();
    }

    private static readonly string[] ExpectedColumns =
    [
        "refresh_attempts:attempt_id:uuid:true:", "refresh_attempts:sequence:bigint:true:a",
        "refresh_attempts:started_at:timestamp with time zone:true:", "refresh_attempts:finished_at:timestamp with time zone:false:",
        "refresh_attempts:status:text:true:", "refresh_attempts:error_code:text:false:",
        "schema_version:version:integer:true:", "schema_version:applied_at:timestamp with time zone:true:",
        "snapshots:snapshot_id:uuid:true:", "snapshots:attempt_id:uuid:true:", "snapshots:payload:jsonb:true:",
        "snapshots:original_xml:bytea:true:", "snapshots:source_kind:text:true:", "snapshots:source_url:text:false:",
        "snapshots:source_sha256:text:true:", "snapshots:fetched_at:timestamp with time zone:true:",
        "snapshots:published_at:timestamp with time zone:true:", "snapshots:source_modified_at:timestamp with time zone:false:",
        "state:singleton:boolean:true:", "state:current_snapshot_id:uuid:false:"
    ];

    private static readonly string[] ExpectedConstraints =
    [
        "refresh_attempts:attempt_status:c:true", "refresh_attempts:attempt_error:c:true",
        "refresh_attempts:attempt_terminal:c:true", "refresh_attempts:attempt_abandoned:c:true",
        "refresh_attempts:refresh_attempts_pkey:p:true", "refresh_attempts:refresh_attempts_sequence_key:u:true",
        "schema_version:schema_version_pkey:p:true", "schema_version:schema_version_one:c:true",
        "snapshots:snapshots_pkey:p:true", "snapshots:snapshots_attempt_id_key:u:true", "snapshots:snapshots_attempt_id_fkey:f:true",
        "snapshots:snapshot_payload:c:true", "snapshots:snapshot_bytes:c:true", "snapshots:snapshot_kind:c:true",
        "snapshots:snapshot_hash:c:true", "snapshots:snapshot_provenance:c:true",
        "state:state_pkey:p:true", "state:state_singleton:c:true", "state:state_current_snapshot_id_fkey:f:true"
    ];

    // PostgreSQL canonical definitions. Unknown catalog shapes fail closed; initialization never repairs them.
    private static readonly IReadOnlyDictionary<string, string> ExpectedDefinitions = new Dictionary<string, string>
    {
        ["attempt_abandoned"] = "CHECK (((status <> 'abandoned'::text) OR (error_code = 'abandoned'::text)))",
        ["attempt_error"] = "CHECK ((error_code = ANY (ARRAY['snapshot_malformed'::text, 'source_rejected'::text, 'source_timeout'::text, 'db_unavailable'::text, 'publication_unknown'::text, 'cancelled'::text, 'abandoned'::text])))",
        ["attempt_status"] = "CHECK ((status = ANY (ARRAY['running'::text, 'success'::text, 'failed'::text, 'abandoned'::text])))",
        ["attempt_terminal"] = "CHECK ((((status = 'running'::text) AND (finished_at IS NULL) AND (error_code IS NULL)) OR ((status = 'success'::text) AND (finished_at IS NOT NULL) AND (error_code IS NULL)) OR ((status = ANY (ARRAY['failed'::text, 'abandoned'::text])) AND (finished_at IS NOT NULL) AND (error_code IS NOT NULL))))",
        ["refresh_attempts_pkey"] = "PRIMARY KEY (attempt_id)",
        ["refresh_attempts_sequence_key"] = "UNIQUE (sequence)",
        ["schema_version_one"] = "CHECK ((version = 1))",
        ["schema_version_pkey"] = "PRIMARY KEY (version)",
        ["snapshot_bytes"] = "CHECK (((octet_length(original_xml) >= 1) AND (octet_length(original_xml) <= 16777216)))",
        ["snapshot_hash"] = "CHECK ((source_sha256 ~ '^[0-9a-f]{64}$'::text))",
        ["snapshot_kind"] = "CHECK ((source_kind = ANY (ARRAY['file'::text, 'http'::text])))",
        ["snapshot_payload"] = "CHECK ((jsonb_typeof(payload) = 'object'::text))",
        ["snapshot_provenance"] = "CHECK ((((source_kind = 'file'::text) AND (source_url IS NULL) AND (source_modified_at IS NULL)) OR ((source_kind = 'http'::text) AND (source_url IS NOT NULL) AND (source_url = '"
            + TimetableParser.DefaultUrl.Replace("'", "''") + "'::text))))",
        ["snapshots_attempt_id_key"] = "UNIQUE (attempt_id)",
        ["snapshots_pkey"] = "PRIMARY KEY (snapshot_id)",
        ["state_pkey"] = "PRIMARY KEY (singleton)",
        ["state_singleton"] = "CHECK (singleton)"
    };
}
