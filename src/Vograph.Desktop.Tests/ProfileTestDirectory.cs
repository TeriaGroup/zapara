using Microsoft.Data.Sqlite;
using Xunit;

namespace Vograph.Desktop.Tests;

internal sealed class ProfileTestDirectory : IDisposable
{
    // The session scratchpad can already have a long path. Leave room for the
    // 64-character server key and user UUID before SQLite adds journal/WAL suffixes.
    public string Root { get; } = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
