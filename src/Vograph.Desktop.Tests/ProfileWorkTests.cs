using Vograph.Desktop.Services.Profiles;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class ProfileWorkTests
{
    [Fact]
    public async Task Suspension_invalidates_outer_await_and_resume_does_not_revalidate_it()
    {
        var lifetime = new ProfileWorkLifetime();
        using var old = lifetime.Enter();
        Assert.True(old.IsCurrent);
        lifetime.Suspend();
        Assert.False(old.IsCurrent);
        Assert.False(lifetime.IsAccepting);
        Assert.False(lifetime.WhenIdleAsync(TestContext.Current.CancellationToken).IsCompleted);
        lifetime.Resume();
        using (var nested = lifetime.Enter()) Assert.False(nested.IsCurrent);
        old.Dispose();
        using (var fresh = lifetime.Enter()) Assert.True(fresh.IsCurrent);
        await lifetime.WhenIdleAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Posted_callback_is_owned_before_dispatch_and_until_continuation_finishes()
    {
        var lifetime = new ProfileWorkLifetime();
        Action? callback = null;
        var published = false;
        lifetime.Post(a => callback = a, () => { published = true; return Task.CompletedTask; }, e => throw e);
        Assert.Equal(1, lifetime.Outstanding);
        lifetime.Suspend();
        var idle = lifetime.WhenIdleAsync(TestContext.Current.CancellationToken);
        Assert.False(idle.IsCompleted);
        callback!();
        await idle;
        Assert.False(published);
    }

    [Fact]
    public void Profile_paths_validate_scope_and_keep_guest_at_root()
    {
        using var directory = new ProfileTestDirectory();
        var user = Guid.NewGuid();
        var key = new string('A', 64);
        Assert.Equal(Path.Combine(directory.Root, "vograph.db"), ProfileDescriptor.Guest(directory.Root).DatabasePath);
        Assert.Equal(Path.Combine(directory.Root, "profiles", key, user.ToString("D"), "vograph.db"),
            ProfileDescriptor.Account(directory.Root, key, user).DatabasePath);
        Assert.Throws<ArgumentException>(() => ProfileDescriptor.Account(directory.Root, "../escape", user));
        Assert.Throws<ArgumentException>(() => ProfileDescriptor.Account(directory.Root, key, Guid.Empty));
    }
}
