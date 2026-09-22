using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Zapara.Server.Accounts;

internal static class AccountsSchemaShape
{
    // PostgreSQL 16 catalog receipt, excluding OIDs, owners, data, and schema name.
    // Native pretty deparsing keeps Boolean precedence/literals while removing AND
    // grouping differences introduced by pg_dump/pg_restore. Migration SQL is unchanged.
    internal const string ExpectedFingerprint = "48b87b1c78c73ce20f806e347fe9872cd3e4aa19b1e90329c4da497a8aebfd0a";

    internal const string ExternalFingerprint = "03822733af666d21ffbfd3dcd50adeb0a1bd552547fb43e93fabaea4ed4441b2";

    internal const string RecoveryFingerprint = "c85622246eefea39f2d870999e1b3a67d7871050b7a2ec36409d9ed9a34402a7";

    internal const string LifecycleFingerprint = "45e2f35d493e4ca86f5f225953fcee99db4990d3596e407a281d48f477adac98";
    internal const string WebFingerprint = "c76e954c44b11d1a01749c896790f995e8774f990d8a8cbb9d22f2dde04dc08e";
    internal const string PushFingerprint = "21c496f50e0bc3fe75b690080572dc0b7aa1b3319cb6f5d0fc227f46aa31c608";

    internal static async Task VerifyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken ct, int version = 1)
    {
        var fingerprint = await FingerprintAsync(connection, transaction, schema, ct);
        var expected = version switch
        {
            1 => ExpectedFingerprint,
            2 => ExternalFingerprint,
            3 => RecoveryFingerprint,
            4 => LifecycleFingerprint,
            5 => WebFingerprint,
            6 => PushFingerprint,
            _ => throw AccountsMigrations.InvalidSchema()
        };
        if (fingerprint != expected) throw AccountsMigrations.InvalidSchema();
    }

    internal static async Task<long> ObjectCountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM pg_depend d JOIN pg_namespace n ON n.oid=d.refobjid
            WHERE d.refclassid='pg_namespace'::regclass AND n.nspname=@schema
            """, connection, transaction);
        command.Parameters.AddWithValue("schema", schema);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }

    internal static async Task<string> FingerprintAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT string_agg(v, E'\n' ORDER BY v COLLATE "C") FROM (
              SELECT 'relation|'||c.relname||'|'||c.relkind::text||'|'||c.relrowsecurity||'|'||c.relforcerowsecurity AS v
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'column|'||c.relname||'|'||a.attnum||'|'||a.attname||'|'||format_type(a.atttypid,a.atttypmod)||'|'||a.attnotnull||'|'||coalesce(pg_get_expr(d.adbin,d.adrelid),'')
                FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
                WHERE n.nspname=@schema AND a.attnum>0 AND NOT a.attisdropped AND c.relkind='r'
              UNION ALL
              SELECT 'constraint|'||c.relname||'|'||k.conname||'|'||pg_get_constraintdef(k.oid,true)||'|'||k.convalidated
                FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'index|'||c.relname||'|'||pg_get_indexdef(i.indexrelid)||'|'||i.indisvalid
                FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'trigger|'||t.tgname||'|'||pg_get_triggerdef(t.oid) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND NOT t.tgisinternal
              UNION ALL
              SELECT 'routine|'||p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'type|'||t.typname||'|'||t.typtype::text FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'namespace_dependencies|'||count(*) FROM pg_depend d JOIN pg_namespace n ON n.oid=d.refobjid
                WHERE d.refclassid='pg_namespace'::regclass AND n.nspname=@schema
            ) shape
            """, connection, transaction);
        command.Parameters.AddWithValue("schema", schema);
        var value = (string?)await command.ExecuteScalarAsync(ct) ?? "";
        value = value.Replace($"\"{schema}\".", "__SCHEMA__.", StringComparison.Ordinal)
            .Replace(schema + ".", "__SCHEMA__.", StringComparison.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
