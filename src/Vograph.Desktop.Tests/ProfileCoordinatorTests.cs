using System.Data;
using Vograph.Core.Models;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Vograph.Desktop.Shell;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class ProfileCoordinatorTests
{
    [Fact]
    public async Task Guest_A_B_guest_have_distinct_real_SQLite_rowsets_and_shared_preferences()
    {
        using var directory = new ProfileTestDirectory();
        var guest = AppServices.Create(directory.Root, () => false);
        guest.AllowNetwork = false;
        var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new Uri("http://127.0.0.1/profile/"));
        using var vault = new AccountMemoryVault(client.Scope.Key);
        var coordinator = Coordinator(guest, client, vault);
        try
        {
            Seed(guest, "guest");
            guest.Prefs.Theme = ThemeChoice.Dark;
            guest.Prefs.Save();
            var prefs = File.ReadAllBytes(Path.Combine(directory.Root, "ui.json"));
            Assert.True((await coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken)).Committed);
            var a = coordinator.Current.Services;
            Assert.NotSame(guest, a);
            Assert.Same(guest.Shared, a.Shared);
            Assert.Empty(a.Db.GetFriends());
            Assert.Empty(a.Db.GetOverrides());
            Assert.Empty(a.Homework.GetAll());
            Assert.NotEqual("guest", a.Settings.MyGroupId);
            Seed(a, "A");
            var bId = Guid.NewGuid();
            handler.Send = (_, _) => Task.FromResult(Json(Session(2, Guid.NewGuid(), bId)));
            Assert.True((await coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken)).Committed);
            var b = coordinator.Current.Services;
            Assert.Empty(b.Db.GetFriends());
            Assert.Empty(b.Db.GetOverrides());
            Assert.Empty(b.Homework.GetAll());
            Seed(b, "B");
            Assert.True((await coordinator.LogoutAsync(TestContext.Current.CancellationToken)).Committed);
            Assert.Equal("guest", coordinator.Current.Services.Settings.MyGroupId);
            Assert.Equal("guest", Assert.Single(coordinator.Current.Services.Db.GetFriends()).MemberNames);
            Assert.Equal("guest", Assert.Single(coordinator.Current.Services.Db.GetOverrides()).DisplayName);
            Assert.Equal("guest", Assert.Single(coordinator.Current.Services.Homework.GetAll()).Text);
            using (var cachedA = coordinator.Current.Services.CreateProfile(a.Profile))
            {
                Assert.Equal("A", cachedA.Settings.MyGroupId);
                Assert.Equal("A", Assert.Single(cachedA.Homework.GetAll()).Text);
            }
            using (var cachedB = coordinator.Current.Services.CreateProfile(b.Profile))
            {
                Assert.Equal("B", cachedB.Settings.MyGroupId);
                Assert.Equal("B", Assert.Single(cachedB.Db.GetOverrides()).DisplayName);
            }
            Assert.Equal(prefs, File.ReadAllBytes(Path.Combine(directory.Root, "ui.json")));
            Assert.Equal(ThemeChoice.Dark, coordinator.Current.Services.Prefs.Theme);
            Assert.True(guest.IsClosed && a.IsClosed && b.IsClosed);
            Assert.Null(vault.Entry);
        }
        finally { await coordinator.ExitAsync(); }
    }

    [Fact]
    public async Task Busy_gate_rolls_back_without_stopping_shell_closing_DB_or_activating_vault()
    {
        using var directory = new ProfileTestDirectory();
        var app = AppServices.Create(directory.Root, () => false);
        app.AllowNetwork = false;
        using var http = new HttpClient(new AccountClientHandler());
        using var client = new AccountHttpClient(http, new Uri("http://127.0.0.1/"));
        using var vault = new AccountMemoryVault(client.Scope.Key);
        var coordinator = Coordinator(app, client, vault, TimeSpan.FromMilliseconds(60));
        await app.CoreGate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            var result = await coordinator.LoginAsync("Test.User", Password, TestContext.Current.CancellationToken);
            Assert.False(result.Committed);
            Assert.Equal(ProfileFailure.Busy, result.Snapshot.Failure);
            Assert.Null(vault.Entry);
            Assert.Same(app, coordinator.Current.Services);
            Assert.False(coordinator.Current.Shell.IsStopped);
            Assert.False(coordinator.Current.Shell.IsSuspended);
            Assert.Equal(ConnectionState.Open, app.Db.Connection.State);
        }
        finally { app.CoreGate.Release(); await coordinator.ExitAsync(); }
    }

    internal static ProfileSwitchCoordinator Coordinator(AppServices app, AccountHttpClient client, IAccountSessionVault vault,
        TimeSpan? timeout = null, Func<ProfileDescriptor, AppServices>? factory = null, Action<ProfileRoot>? publish = null)
        => new(new(app, new ShellViewModel(app)), client, vault, DeviceId,
            action => { action(); return Task.CompletedTask; }, publish ?? (_ => { }), factory, timeout);

    internal static void Seed(AppServices app, string value)
    {
        var settings = app.Db.GetSettings();
        settings.MyGroupId = value;
        app.Db.SaveSettings(settings);
        app.Db.InsertFriend(new FriendGroup { GroupName = value, MemberNames = value, Enabled = true });
        app.Overrides.AddOrUpdate("subject", "global", value, value);
        app.Homework.AddHomework("subject", value, 1, Now.UtcDateTime);
    }
}
