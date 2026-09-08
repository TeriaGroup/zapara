using Vograph.Desktop.Services.Profiles;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ProfileInstallationTests
{
    [Fact]
    public void Installation_UUID_is_persistent_and_corruption_is_not_silently_replaced()
    {
        using var directory = new ProfileTestDirectory();
        var first = ProfileInstallation.LoadOrCreate(directory.Root);
        Assert.NotEqual(Guid.Empty, first);
        Assert.Equal(first, ProfileInstallation.LoadOrCreate(directory.Root));
        File.WriteAllText(Path.Combine(directory.Root, "installation.id"), "broken");
        Assert.Throws<InvalidDataException>(() => ProfileInstallation.LoadOrCreate(directory.Root));
    }
}
