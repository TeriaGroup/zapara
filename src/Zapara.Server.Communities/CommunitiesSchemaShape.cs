using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace Zapara.Server.Communities;

internal static class CommunitiesSchemaShape
{
    // Consume complete catalog tokens, including literals, before comparing qualifiers.
    private static readonly Regex CatalogTokens = new(
        """[eE]'(?:\\.|''|[^'\\])*'|'(?:''|[^'])*'|\$(?<tag>[A-Za-z_][A-Za-z_0-9]*|)\$[\s\S]*?\$\k<tag>\$|"(?:""|[^"])*"|[\p{L}\p{N}_$\u0080-\uFFFF]+""",
        RegexOptions.CultureInvariant);
    // PostgreSQL 16 catalog, independent of fixture schema names, OIDs, owners and row content.
    // PostgreSQL's pretty constraint deparser preserves precedence and literals while
    // making CHECK grouping stable across a real pg_dump/pg_restore round trip.
    internal const string BaselineExpected = "428ddb0b7fdcde45dd0a6732d098af13628b8eea5de6d83dc78cfb2b2ae043c9";
    internal const string Expected = "f7680b76b7cdd31d71f3017925d4e2da5500572549806d0c1877a9eb2fc377a3";
    internal static async Task VerifyAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CommunitiesConfiguration options, CancellationToken ct, int version = 2)
    {
        var actual = await FingerprintAsync(connection, tx, options, ct);
        if (actual != (version == 1 ? BaselineExpected : Expected)) throw new InvalidOperationException("Схема Communities не соответствует известной миграции.");
    }
    internal static async Task<string> FingerprintAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CommunitiesConfiguration options, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT coalesce(string_agg(v, E'\n' ORDER BY v COLLATE "C"),'') FROM (
              SELECT 'relation|'||c.relname||'|'||c.relkind::text||'|'||c.relpersistence::text||'|'||c.relrowsecurity||'|'||c.relforcerowsecurity||'|'||coalesce(array_to_string(c.reloptions,','),'') v
                FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'column|'||c.relname||'|'||a.attnum||'|'||a.attname||'|'||format_type(a.atttypid,a.atttypmod)||'|'||a.attnotnull||'|'||a.attidentity::text||'|'||a.attgenerated::text||'|'||a.attisdropped||'|'||coalesce(pg_get_expr(d.adbin,d.adrelid),'')
                FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum
                WHERE n.nspname=@schema AND a.attnum>0 AND c.relkind='r'
              UNION ALL
              SELECT 'constraint|'||c.relname||'|'||k.conname||'|'||pg_get_constraintdef(k.oid,true)||'|'||k.convalidated
                FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'index|'||c.relname||'|'||pg_get_indexdef(i.indexrelid)||'|'||i.indisvalid||'|'||i.indisready
                FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'trigger|'||t.tgname||'|'||pg_get_triggerdef(t.oid)||'|'||t.tgenabled::text FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
                JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND NOT t.tgisinternal
              UNION ALL
              SELECT 'routine|'||p.proname||'|'||pg_get_functiondef(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'type|'||t.typname||'|'||t.typtype::text FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'rule|'||r.rulename||'|'||pg_get_ruledef(r.oid) FROM pg_rewrite r JOIN pg_class c ON c.oid=r.ev_class
                JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'policy|'||p.polname FROM pg_policy p JOIN pg_class c ON c.oid=p.polrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema
              UNION ALL
              SELECT 'dependencies|'||count(*) FROM pg_depend d JOIN pg_namespace n ON n.oid=d.refobjid
                WHERE d.refclassid='pg_namespace'::regclass AND n.nspname=@schema
            ) shape
            """, connection, tx);
        command.Parameters.AddWithValue("schema", options.Schema);
        var value = (string)(await command.ExecuteScalarAsync(ct))!;
        value = NormalizeQualifiers(value, options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    internal static string NormalizeQualifiers(string value, CommunitiesConfiguration options)
    {
        return CatalogTokens.Replace(value, match =>
        {
            var end = match.Index + match.Length;
            if (end == value.Length || value[end] != '.') return match.Value;
            if (match.Value == options.Schema || match.Value == options.QuotedSchema) return "__COM__";
            if (match.Value == options.Accounts.Schema || match.Value == options.Accounts.QuotedSchema) return "__ACCOUNTS__";
            return match.Value;
        });
    }
}
