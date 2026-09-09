using System.Net;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Features.Account;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Xunit;
using Zapara.Contracts.Accounts;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed partial class AccountUiFlowTests
{
    [Fact]
    public async Task Production_capabilities_do_not_offer_registration()
    {
        await using var f = new Fixture();
        f.Vm.ToggleRegistrationCommand.Execute(null);
        Assert.False(f.Vm.Registration);
        f.Handler.Send = (_, _) => Task.FromResult(Json(new
            { password = true, vk = false, yandex = false, registration = false, recovery = false }));
        await f.Vm.InitializeAsync();
        f.Vm.ToggleRegistrationCommand.Execute(null);
        Assert.False(f.Vm.Registration);
        Assert.Equal(1, f.Handler.Calls);
    }

    [Fact]
    public async Task Registration_unavailable_has_actionable_Russian_status()
    {
        await using var f = new Fixture();
        await f.Vm.InitializeAsync();
        f.Handler.Send = (_, _) => Task.FromResult(Json(
            new AccountError("ignored", 503, "registration_unavailable"), HttpStatusCode.ServiceUnavailable));
        f.Vm.Registration = true;
        f.Vm.Username = "Test.User"; f.Vm.Password = Password;
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.Contains("Регистрация на этом сервере недоступна", f.Vm.Status);
        Assert.Empty(f.Vm.Password);
        Assert.Null(f.Vault.Entry);
    }

    [Fact]
    public async Task Register_201_does_not_login_and_secrets_are_cleared()
    {
        await using var f = new Fixture();
        await f.Vm.InitializeAsync();
        f.Vm.Registration = true;
        f.Vm.Username = "test.user"; f.Vm.Password = Password;
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsGuest);
        Assert.Null(f.Vault.Entry);
        Assert.Equal("", f.Vm.Password);
        Assert.Contains("создан", f.Vm.Status);
        Assert.Equal(2, f.Handler.Calls);
    }

    [Theory]
    [InlineData("абв", "long enough password")]
    [InlineData("valid.user", "short")]
    [InlineData("ab", "long enough password")]
    public async Task Invalid_fields_do_not_send_requests(string username, string password)
    {
        await using var f = new Fixture();
        await f.Vm.InitializeAsync();
        f.Vm.Username = username; f.Vm.Password = password;
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(1, f.Handler.Calls);
        Assert.Equal("", f.Vm.Password);
        Assert.Contains("3–32", f.Vm.Status);
    }

    [Fact]
    public async Task Login_logout_commands_do_not_self_drain_and_guest_rows_survive()
    {
        await using var f = new Fixture();
        ProfileCoordinatorTests.Seed(f.Profiles.Current.Services, "guest-canary");
        await f.Login();
        Assert.False(f.Vm.IsGuest);
        Assert.Empty(f.Profiles.Current.Services.Homework.GetAll());
        Assert.Equal("Test.User", f.Vm.AccountName);
        f.Vm.RequestLogoutCommand.Execute(null);
        await f.Vm.LogoutCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(f.Vm.IsGuest);
        Assert.Equal("guest-canary", Assert.Single(f.Profiles.Current.Services.Homework.GetAll()).Text);
        Assert.Contains("не подтверждён", f.Vm.Status);
    }

    [Theory]
    [InlineData(401, "invalid_credentials", "Неверный")]
    [InlineData(429, "rate_limited", "Слишком")]
    public async Task Early_login_failure_is_consumed_without_changed_event(int code, string error, string text)
    {
        await using var f = new Fixture();
        await f.Vm.InitializeAsync();
        f.Handler.Send = (_, _) => Task.FromResult(Json(new AccountError("ignored", code, error), (HttpStatusCode)code));
        f.Vm.Username = "Test.User"; f.Vm.Password = Password;
        await f.Vm.SubmitCommand.ExecuteAsync(null);
        Assert.Contains(text, f.Vm.Status);
        Assert.True(f.Vm.IsGuest);
        Assert.Equal("", f.Vm.Password);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("all")]
    [InlineData("device")]
    public async Task Credential_mutation_exits_matching_profile_without_manual_vault_clear(string action)
    {
        await using var f = new Fixture();
        await f.Login();
        if (action == "password")
        {
            f.Vm.CurrentPassword = Password; f.Vm.NewPassword = Password + "new";
            await f.Vm.ChangePasswordCommand.ExecuteAsync(null);
        }
        else if (action == "all") await f.Vm.RevokeAllCommand.ExecuteAsync(null);
        else
        {
            await f.Vm.LoadDevicesCommand.ExecuteAsync(null);
            await f.Vm.RevokeDeviceCommand.ExecuteAsync(Assert.Single(f.Vm.Devices));
        }
        Assert.True(f.Vm.IsGuest);
        Assert.Null(f.Vault.Entry);
        Assert.Equal("", f.Vm.CurrentPassword);
        Assert.Equal("", f.Vm.NewPassword);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    public async Task Device_pagination_continues_past_100_only_on_user_request(int pageSize)
    {
        await using var f = new Fixture();
        await f.Login();
        var devices = Enumerable.Range(1, 102).Select(i => new DeviceResponse(
            new Guid(i, 0, 0, new byte[8]), DeviceId, "Device " + i,
            "windows", Now, Now, Now.AddDays(30), false)).ToArray();
        var queries = new List<string>();
        var pagesToBoundary = 100 / pageSize;
        f.Handler.Send = (request, _) =>
        {
            queries.Add(request.RequestUri!.PathAndQuery);
            var page = queries.Count;
            return Task.FromResult(Json(page <= pagesToBoundary
                ? new DevicesResponse(devices.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                    new string((char)('a' + page), 55))
                : new DevicesResponse([devices[99], devices[100], devices[101]], null)));
        };

        await f.Vm.LoadDevicesCommand.ExecuteAsync(null);
        Assert.Single(queries);
        Assert.Equal(pageSize, f.Vm.Devices.Count);
        for (var page = 2; page <= pagesToBoundary; page++)
        {
            await f.Vm.MoreDevicesCommand.ExecuteAsync(null);
            Assert.Equal(page, queries.Count);
            Assert.Equal(page * pageSize, f.Vm.Devices.Count);
        }
        Assert.Equal(100, f.Vm.Devices.Count);
        Assert.True(f.Vm.HasMore);

        await f.Vm.MoreDevicesCommand.ExecuteAsync(null);
        Assert.Equal(102, f.Vm.Devices.Count);
        Assert.Equal(devices.Select(d => d.FamilyId), f.Vm.Devices.Select(d => d.FamilyId));
        Assert.False(f.Vm.HasMore);
        Assert.Equal(pagesToBoundary + 1, queries.Count);
        Assert.Equal("/api/v1/account/devices?limit=10", queries[0]);
        for (var page = 1; page < queries.Count; page++)
            Assert.Equal("/api/v1/account/devices?limit=10&cursor=" + new string((char)('a' + page), 55), queries[page]);

        await f.Vm.MoreDevicesCommand.ExecuteAsync(null);
        Assert.Equal(pagesToBoundary + 1, queries.Count);
        Assert.Equal(102, f.Vm.Devices.Count);
    }

    [Fact]
    public async Task Unconfigured_initialization_is_honest_and_disposal_clears_fields()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        using var vm = new AccountPanelViewModel();
        await vm.InitializeAsync();
        Assert.False(vm.CanAct);
        Assert.Equal("Сервер аккаунтов не настроен", vm.Status);
        vm.Password = Password; vm.Dispose();
        Assert.Empty(vm.Password);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ProfileTestDirectory directory = new();
        private readonly HttpClient http;
        private readonly AccountHttpClient client;
        public AccountClientHandler Handler { get; } = new();
        public AccountMemoryVault Vault { get; }
        public ProfileSwitchCoordinator Profiles { get; }
        public AccountPanelViewModel Vm { get; }
        public Fixture()
        {
            var services = AppServices.Create(directory.Root, () => false);
            services.AllowNetwork = false;
            http = new(Handler); client = new(http, new Uri("http://127.0.0.1/"));
            Vault = new(client.Scope.Key);
            Profiles = ProfileCoordinatorTests.Coordinator(services, client, Vault, TimeSpan.FromMilliseconds(500));
            Vm = new(Profiles, new AccountUiService(client, Vault, Profiles));
            Handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/auth/capabilities" => Json(new { password = true, vk = false, yandex = false, registration = true, recovery = false }),
                "/api/v1/auth/register" => Json(User, HttpStatusCode.Created),
                "/api/v1/auth/login" => Json(new SessionResponse(User, FamilyId, Token("za_"), Token("zr_"),
                    "Bearer", DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow.AddDays(30))),
                "/api/v1/account/devices" => Json(new DevicesResponse([new DeviceResponse(FamilyId, DeviceId,
                    "Windows", "windows", Now, Now, Now.AddDays(30), true)], null)),
                _ => new HttpResponseMessage(HttpStatusCode.NoContent)
            });
        }
        public async Task Login()
        {
            await Vm.InitializeAsync();
            Vm.Username = "Test.User"; Vm.Password = Password;
            await Vm.SubmitCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            Vm.Dispose(); await Profiles.ExitAsync(); await Profiles.RemoteLogouts;
            Vault.Dispose(); client.Dispose(); http.Dispose(); directory.Dispose();
        }
    }
}
