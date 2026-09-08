using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;

namespace Vograph.Desktop.Tests;

internal sealed class ProfileHarness : IAsyncDisposable
{
    internal readonly ProfileTestDirectory Directory = new();
    internal readonly AccountClientHandler Handler = new();
    internal readonly HttpClient Http;
    internal readonly AccountHttpClient Client;
    internal readonly IAccountSessionVault Vault;
    internal readonly AppServices Guest;
    internal readonly ProfileSwitchCoordinator Coordinator;
    internal ProfileHarness(Func<string, AccountServerScope, IAccountSessionVault>? vaultFactory = null,
        Func<AppServices, ProfileDescriptor, AppServices>? factory = null, Action<ProfileRoot>? publish = null)
    {
        Http = new(Handler);
        Client = new(Http, new Uri("http://127.0.0.1/profile/"));
        Vault = vaultFactory?.Invoke(Directory.Root, Client.Scope) ?? new AccountMemoryVault(Client.Scope.Key);
        Guest = AppServices.Create(Directory.Root, () => false);
        Guest.AllowNetwork = false;
        Coordinator = new(new(Guest, new(Guest)), Client, Vault, AccountClientTestSupport.DeviceId,
            action => { action(); return Task.CompletedTask; }, publish ?? (_ => { }),
            factory is null ? null : profile => factory(Guest, profile), TimeSpan.FromMilliseconds(100), new Clock());
    }
    public async ValueTask DisposeAsync()
    {
        await Coordinator.ExitAsync();
        await Coordinator.RemoteLogouts;
        Client.Dispose(); Http.Dispose();
        (Vault as IDisposable)?.Dispose();
        Directory.Dispose();
    }
    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => AccountClientTestSupport.Now; }
}
