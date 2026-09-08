using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Vograph.Desktop.Shell;

namespace Vograph.Desktop;

public partial class App
{
    private ProfileRoot? startedRoot;
    private AccountHttpClient? accountClient;
    private AccountSessionManager? accountSessions;
    private bool exitReady, exitPending;

    private void ConfigureProfiles(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var url = Environment.GetEnvironmentVariable("VOGRAPH_ACCOUNT_BASE_URL")
            ?? Environment.GetEnvironmentVariable("VOGRAPH_API_BASE_URL");
        if (url is null || !OperatingSystem.IsWindows()) return;
        try
        {
            accountClient = AccountHttpClient.CreateOwned(new Uri(url));
            var root = CurrentRoot!;
            var vaultRoot = Path.Combine(root.Services.DataDir, "credentials");
            Directory.CreateDirectory(vaultRoot);
            var vault = new WindowsAccountSessionVault(vaultRoot, accountClient.Scope);
            accountSessions = new AccountSessionManager(accountClient, vault);
            Profiles = new(root, accountClient, vault, ProfileInstallation.LoadOrCreate(root.Services.DataDir),
                async action => await Dispatcher.UIThread.InvokeAsync(action), next =>
                {
                    // Clear old private presentation if assigning the new graph unexpectedly fails.
                    try
                    {
                        CurrentRoot = next;
                        Services = next.Services;
                        next.Shell.Shutdown = () => desktop.TryShutdown();
                        window.DataContext = next.Shell;
                    }
                    catch { window.DataContext = null; throw; }
                });
            root.Services.Shared.SetAccountPanel(new Features.Account.AccountPanelViewModel(Profiles,
                new AccountUiService(accountClient, vault, Profiles)));
            Profiles.Changed += snapshot =>
            {
                if (window.IsVisible && snapshot.Phase == ProfilePhase.Idle && snapshot.Failure is null) StartCurrentProfile();
            };
            desktop.ShutdownRequested += async (_, args) =>
            {
                if (exitReady) return;
                args.Cancel = true;
                if (exitPending) return;
                exitPending = true;
                try
                {
                    await Profiles.ExitAsync();
                    await Profiles.RemoteLogouts;
                    accountClient.Dispose();
                    exitReady = true;
                    desktop.Shutdown();
                }
                catch (Exception ex) { Services!.Log.Error("profile exit", ex); exitPending = false; }
            };
            window.Closing += (_, args) =>
            {
                if (exitReady) return;
                args.Cancel = true;
                desktop.TryShutdown();
            };
        }
        catch (Exception ex)
        {
            accountClient?.Dispose();
            Services!.Log.Warn("account configuration unavailable: " + ex.GetType().Name);
            // No fabricated production endpoint and no credential fallback. Guest remains usable.
        }
    }

    private void StartCurrentProfile()
    {
        var root = Profiles?.Current ?? CurrentRoot;
        if (root is null || ReferenceEquals(root, startedRoot) || !root.Services.Work.IsAccepting) return;
        startedRoot = root;
        StartCurrentProfile(root, accountClient, accountSessions);
        root.Services.Work.Post(a => Dispatcher.UIThread.Post(a), () => root.Shell.StartAsync(root.Services.AllowNetwork),
            ex => root.Services.Log.Error("profile startup callback", ex));
    }

    /// <summary>Guest may start LAN. Account never does; it attaches private sync with the vault session instead.</summary>
    internal static void StartCurrentProfile(ProfileRoot root, AccountHttpClient? accounts, AccountSessionManager? sessions,
        Func<Uri, PrivateSyncHttpClient>? syncHttp = null)
    {
        root.Services.NotificationScheduler.Start();
        if (root.Services.Profile.IsGuest && root.Services.Prefs.LanSync)
        {
            try { root.Services.LanSync.Start(); }
            catch (Exception ex)
            {
                root.Services.Log.Error("lan sync start", ex);
                root.Services.Toasts.Error(root.Services.LanSync.StartFailureText(ex));
            }
            return;
        }
        if (root.Services.Profile.IsGuest || root.Services.PrivateSync is not { } sync || accounts is null || sessions is null)
            return;
        if (sync.IsAttached) return;
        var http = (syncHttp ?? (uri => PrivateSyncHttpClient.CreateOwned(uri)))(accounts.Scope.BaseUri);
        if (http.Scope.Key != accounts.Scope.Key)
        {
            http.Dispose();
            throw new ArgumentException("Клиент синхронизации должен совпадать с сервером аккаунта.");
        }
        sync.Attach(http, async ct => (await sessions.GetValidSessionAsync(ct).ConfigureAwait(false)).AccessToken, background: true);
    }
}
