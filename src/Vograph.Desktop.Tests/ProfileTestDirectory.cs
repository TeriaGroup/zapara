using Microsoft.Data.Sqlite;
using Xunit;

namespace Vograph.Desktop.Tests;

internal sealed class ProfileTestDirectory : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "zapara-profile-test-" + Guid.NewGuid().ToString("N"));
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
