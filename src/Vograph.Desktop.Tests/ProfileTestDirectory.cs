using Microsoft.Data.Sqlite;
using Xunit;

namespace Vograph.Desktop.Tests;

internal sealed class ProfileTestDirectory : IDisposable
{
    // The session scratchpad can already have a long path. Leave room for the
    // 64-character server key and user UUID before SQLite adds journal/WAL suffixes.
    public string Root { get; } = CreateRoot();
    private static string CreateRoot()
    {
        var configured = Environment.GetEnvironmentVariable("VOGRAPH_TEST_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(configured)) return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var root = Path.GetFullPath(configured);
        if (!root.StartsWith(@"\\?\", StringComparison.Ordinal)) root = @"\\?\" + root;
        return Path.Combine(root, Guid.NewGuid().ToString("N"));
    }
    public ProfileTestDirectory() => Directory.CreateDirectory(Root);
    public void Dispose()
    {
        // Clear only this fixture's pools; never interrupt another worker's databases.
        foreach (var path in Directory.EnumerateFiles(Root, "*.db", SearchOption.AllDirectories))
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            SqliteConnection.ClearPool(connection);
        }
        Directory.Delete(Root, true);
        Assert.False(Directory.Exists(Root));
    }
}
