using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Zapara.Server.Accounts;

internal static class AccountsSchemaShape
{
    // PostgreSQL 16 catalog receipt, excluding OIDs, owners, data, and schema name.
    internal const string ExpectedFingerprint = "162d102cb494d8f357510ecbf5b187e6ed2ba278fdae0c75b289103ef4d16edd";

    internal const string ExternalFingerprint = "1114fe37d56ed7d96ed43746bfada049756f69803149b21263863b6bfa282804";

    internal const string RecoveryFingerprint = "5988e7f88eada2dd324bc721d00d028ecb58ae30b4c1ec82ca30f5f92ebb9887";

    internal const string LifecycleFingerprint = "281c696405ffbef85a4f29353a8f95918100eea376b11f3a99922cecabb8daa8";

    internal static async Task VerifyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string schema, CancellationToken ct, int version = 1)
    {
        var fingerprint = await FingerprintAsync(connection, transaction, schema, ct);
        var expected = version switch
        {
            1 => ExpectedFingerprint,
            2 => ExternalFingerprint,
            3 => RecoveryFingerprint,
            4 => LifecycleFingerprint,
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
              SELECT 'constraint|'||c.relname||'|'||k.conname||'|'||pg_get_constraintdef(k.oid)||'|'||k.convalidated
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
