using System.Text.RegularExpressions;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#154: every transaction on the shared desktop SQLite connection runs inside Database.EnterWrite, and the lock
/// order is CoreGate, then the write lock, never the reverse. A source scan, so a new transaction site or a gate taken
/// inside a transaction fails here instead of as a rare race in CI.</summary>
public sealed class WriteLockSourceScanTests
{
    private static readonly string[] ClientProjects = ["Vograph.Core", "Vograph.Desktop", "Vograph.Timetable", "Zapara.Client.Domain"];

    private static IEnumerable<(string Path, string[] Lines)> Sources() =>
        ClientProjects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(ResourceKeysTests.RepoRoot(), "src", p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(ResourceKeysTests.RepoRoot(), f).Replace('\\', '/'), File.ReadAllLines(f)));

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("///") || line.TrimStart().StartsWith("*");

    [Fact]
    public void Every_BeginTransaction_is_inside_EnterWrite_with_no_await_after_it()
    {
        var sites = new List<string>();
        var bad = new List<string>();
        foreach (var (path, lines) in Sources())
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsComment(lines[i]) || !Regex.IsMatch(lines[i], @"\bBeginTransaction(Async)?\(")) continue;
                sites.Add($"{path}:{i + 1}");
                var write = Enumerable.Range(Math.Max(0, i - 3), i - Math.Max(0, i - 3)).LastOrDefault(j => lines[j].Contains("EnterWrite()"), -1);
                if (write < 0) { bad.Add($"{path}:{i + 1}: BeginTransaction without EnterWrite() right before it"); continue; }
                // The scope is a Monitor: it must be released on the thread that took it, so no await until the method ends.
                var indent = lines[write].Length - lines[write].TrimStart().Length;
                for (var j = write + 1; j < lines.Length; j++)
                {
                    var t = lines[j];
                    if (t.Trim() == "}" && t.Length - t.TrimStart().Length < indent) break;
                    if (!IsComment(t) && Regex.IsMatch(t, @"\bawait\b")) { bad.Add($"{path}:{j + 1}: await while holding EnterWrite"); break; }
                }
            }
        Assert.Empty(bad);
        Assert.Equal(3, sites.Count); // ParserService ×2, TimetableApiCache: update this count when adding a guarded site
    }

    [Fact]
    public void Raw_transaction_sql_exists_only_in_InTransaction_under_EnterWrite()
    {
        var sql = new Regex(@"""[^""]*\b(SAVEPOINT|BEGIN( IMMEDIATE| EXCLUSIVE| DEFERRED)?( TRANSACTION)?|COMMIT|ROLLBACK|RELEASE|END TRANSACTION)\b"); // SQL here is upper case
        var hits = Sources().SelectMany(s => s.Lines.Select((line, i) => (s.Path, line, i)))
            .Where(x => !IsComment(x.line) && sql.IsMatch(x.line) && !x.line.Contains("BEGIN:V"))
            .ToList();
        Assert.All(hits, h => Assert.Equal("src/Vograph.Core/Services/Sync/PrivateSyncOutbox.cs", h.Path));
        var outbox = File.ReadAllText(Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Core", "Services", "Sync", "PrivateSyncOutbox.cs"));
        var scope = outbox.IndexOf("using (db.EnterWrite())", StringComparison.Ordinal);
        Assert.True(scope > 0, "InTransaction takes the write lock");
        var end = outbox.IndexOf("Changed?.Invoke();", scope, StringComparison.Ordinal);
        Assert.True(end > scope);
        foreach (var statement in new[] { "Exec($\"SAVEPOINT {name}\")", "Exec($\"RELEASE {name}\")", "Exec($\"ROLLBACK TO {name}\")" })
        {
            var at = outbox.IndexOf(statement, StringComparison.Ordinal);
            Assert.True(at > scope && at < end, statement + " inside the EnterWrite scope");
        }
        Assert.Equal(4, hits.Count);
    }

    [Fact]
    public void Nothing_inside_the_write_lock_waits_for_CoreGate_or_the_UI_thread()
    {
        // Core has no CoreGate at all, so Core code under the write lock cannot take it.
        Assert.DoesNotContain(Sources().Where(s => s.Path.StartsWith("src/Vograph.Core/")), s => s.Lines.Any(l => !IsComment(l) && l.Contains("CoreGate")));
        var blocking = new Regex(@"CoreGate|\.Wait\(|\.Result\b|UIThread\.Invoke\b|UIThread\.InvokeAsync|\bawait\b|GetAwaiter\(\)\.GetResult");
        var bad = new List<string>();
        var checkedBodies = 0;
        foreach (var (path, lines) in Sources().Where(s => s.Path.StartsWith("src/Vograph.Desktop/")))
        {
            var text = string.Join('\n', lines);
            foreach (Match m in Regex.Matches(text, @"\.InTransaction\("))
            {
                var body = Balanced(text, m.Index + m.Length - 1);
                checkedBodies++;
                if (blocking.IsMatch(body)) bad.Add($"{path}:{Line(text, m.Index)}: InTransaction body blocks: {blocking.Match(body).Value}");
                // Methods called from the body by name in the same file (ApplyPush, DeleteQueued, ...).
                foreach (Match call in Regex.Matches(body, @"\b([A-Z]\w+)\("))
                {
                    var def = Regex.Match(text, @"(private|internal|public)[^\n;=]*\b" + call.Groups[1].Value + @"\([^)]*\)\s*\n?\s*\{");
                    if (!def.Success) continue;
                    var method = Balanced(text, def.Index + def.Length - 1, '{', '}');
                    if (blocking.IsMatch(method)) bad.Add($"{path}:{Line(text, def.Index)}: {call.Groups[1].Value} runs under the write lock and blocks: {blocking.Match(method).Value}");
                }
            }
        }
        Assert.Empty(bad);
        Assert.True(checkedBodies >= 2, "PrivateSyncCoordinator.ApplyPush and ShellViewModel's 410 abort");
    }

    [Fact]
    public void Every_command_on_the_shared_connection_executes_under_the_write_lock()
    {
        // #161: single commands take the lock in GuardedCommand. A command or connection made any other way would not.
        var bypass = new Regex(@"new\s+Sqlite(Command|Connection)\s*\(|SQLitePCL|raw\.sqlite3_|\.Handle\b.*sqlite");
        var allowed = new[] { "src/Vograph.Core/Services/GuardedSqliteConnection.cs" };
        var hits = Sources().Where(s => !allowed.Contains(s.Path))
            .SelectMany(s => s.Lines.Select((line, i) => (s.Path, line, i)))
            .Where(x => !IsComment(x.line) && bypass.IsMatch(x.line))
            .Select(x => $"{x.Path}:{x.i + 1}: {x.line.Trim()}").ToList();
        Assert.Empty(hits);

        var database = File.ReadAllText(Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Core", "Services", "Database.cs"));
        Assert.Contains("private readonly GuardedSqliteConnection _conn;", database);
        Assert.Contains("_conn = new GuardedSqliteConnection(", database);
        Assert.Contains("public GuardedSqliteConnection.WriteScope EnterWrite() => _conn.EnterWrite();", database);

        // Every execution entry point of the command is overridden and takes the lock; ExecuteReader() and the async
        // variants end up in ExecuteReader(CommandBehavior), ExecuteNonQuery or ExecuteScalar.
        var command = typeof(Vograph.Core.Services.GuardedSqliteConnection).GetNestedType("GuardedCommand", System.Reflection.BindingFlags.NonPublic)!;
        foreach (var (name, args) in new[] { ("ExecuteNonQuery", Type.EmptyTypes), ("ExecuteScalar", Type.EmptyTypes), ("ExecuteReader", new[] { typeof(System.Data.CommandBehavior) }) })
            Assert.Equal(command, command.GetMethod(name, args)!.DeclaringType);
        var guarded = File.ReadAllText(Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Core", "Services", "GuardedSqliteConnection.cs"));
        foreach (var call in new[] { "return base.ExecuteNonQuery(); }", "return base.ExecuteScalar(); }", "return base.ExecuteReader(behavior); }" })
            Assert.Contains("using (owner.EnterWrite()) { Bind(); " + call, guarded);
    }

    private static int Line(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static string Balanced(string text, int open, char o = '(', char c = ')')
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == o) depth++;
            else if (text[i] == c && --depth == 0) return text.Substring(open, i - open + 1);
        }
        return text[open..];
    }
}
