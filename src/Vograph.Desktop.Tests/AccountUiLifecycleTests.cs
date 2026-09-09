using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Features.Account;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalResponses;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class AccountUiLifecycleTests
{
    private static readonly Guid ExportId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly byte[] ExportBytes = Encoding.UTF8.GetBytes("""{"profile":{"username":"Test.User"}}""");
    private static ReauthResponse Proof(string purpose) => new(new string('B', 43), purpose, Now.AddMinutes(5));
    private static ExportJobResponse ReadyJob => new(ExportId, "ready", Now, Now, Now.AddHours(24));

    [Fact]
    public async Task Vk_and_yandex_buttons_follow_capabilities_and_do_not_appear_when_unconfigured()
    {
        await using var hidden = new Fixture(vk: false, yandex: false, recovery: false);
        await hidden.Vm.InitializeAsync();
        Assert.False(hidden.Vm.ShowVkLogin);
        Assert.False(hidden.Vm.ShowYandexLogin);
        Assert.False(hidden.Vm.ShowRecovery);
        await hidden.Vm.StartVkCommand.ExecuteAsync(null);
        await hidden.Vm.ResetPasswordCommand.ExecuteAsync(null);
        Assert.Equal(1, hidden.Handler.Calls);

        await using var shown = new Fixture(vk: true, yandex: false, recovery: true);
        await shown.Vm.InitializeAsync();
        Assert.True(shown.Vm.ShowVkLogin);
        Assert.False(shown.Vm.ShowYandexLogin);
        Assert.True(shown.Vm.ShowRecovery);
        Assert.Contains("Гостевой", shown.Vm.Status);
        Assert.Contains("отдельно", Loc.Current.T("accountIsolation"));
    }

    [Fact]
    public async Task Recovery_request_is_identical_for_known_and_unknown_usernames_and_sends_no_bearer()
    {
        await using var f = new Fixture(recovery: true);
        await f.Vm.InitializeAsync();
        var bodies = new List<string>();
        f.Handler.Send = async (request, ct) =>
        {
            Assert.Equal("/api/v1/auth/password-reset/request", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return Json(new { }, HttpStatusCode.Accepted);
        };
        f.Vm.Username = "no_such_user";
        await f.Vm.ResetPasswordCommand.ExecuteAsync(null);
        var unknownStatus = f.Vm.Status;
        f.Vm.Username = "Test.User";
        await f.Vm.ResetPasswordCommand.ExecuteAsync(null);
        Assert.Equal(unknownStatus, f.Vm.Status);
        Assert.Contains("Сбросить", f.Vm.Status);
        Assert.Equal(2, bodies.Count);
        Assert.Contains("no_such_user", bodies[0], StringComparison.Ordinal);
        Assert.Contains("Test.User", bodies[1], StringComparison.Ordinal);
        Assert.DoesNotContain("Test.User", f.Vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_uses_recent_proof_then_exposes_bytes_and_saves_via_file_dialog()
    {
        await using var f = new Fixture();
        ProfileCoordinatorTests.Seed(f.Profiles.Current.Services, "guest-canary");
        await f.Login();
        var save = Path.Combine(Path.GetTempPath(), "zapara-ui-export-" + Guid.NewGuid().ToString("N") + ".json");
        f.Dialogs.SavePath = save;
        var paths = new List<string>();
        f.Handler.Send = async (request, ct) =>
        {
            paths.Add(request.Method.Method + " " + request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer " + Token("za_"), request.Headers.Authorization?.ToString());
            Assert.DoesNotContain("oauth.vk.com", request.RequestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/v1/account/reauthenticate") return Json(Proof("export"));
            if (path == "/api/v1/account/exports") return Json(ReadyJob, HttpStatusCode.Accepted);
            if (path == "/api/v1/account/exports/" + ExportId.ToString("D") + "/download") return ExportFile();
            throw new InvalidOperationException(path);
        };
        f.Vm.Proof = Password;
        await f.Vm.ExportCommand.ExecuteAsync(null);
        Assert.True(f.Vm.CanDownloadExport);
        Assert.Equal("", f.Vm.Proof);
        Assert.Contains("Скачать", f.Vm.Status);
        await f.Vm.DownloadExportCommand.ExecuteAsync(null);
        Assert.Equal(save, f.Vm.ExportPath);
        Assert.Equal(ExportBytes, f.Vm.ExportPayload);
        Assert.Equal(ExportBytes, await File.ReadAllBytesAsync(save, TestContext.Current.CancellationToken));
        Assert.Equal("zapara-export.json", f.Dialogs.LastSuggestedName);
        Assert.Equal(new[]
        {
            "POST /api/v1/account/reauthenticate", "POST /api/v1/account/exports",
            "GET /api/v1/account/exports/" + ExportId.ToString("D") + "/download"
        }, paths);
        Assert.False(f.Vm.IsGuest);
        File.Delete(save);
    }

    [Fact]
    public async Task Export_without_proof_and_delete_without_confirm_do_not_network()
    {
        await using var f = new Fixture();
        await f.Login();
        var calls = f.Handler.Calls;
        f.Handler.Send = (_, _) => throw new InvalidOperationException("network");
        await f.Vm.ExportCommand.ExecuteAsync(null);
        await f.Vm.DeleteAccountCommand.ExecuteAsync(null);
        Assert.Equal(calls, f.Handler.Calls);
        Assert.False(f.Vm.IsGuest);
        Assert.Contains("3–32", f.Vm.Status);
    }

    [Fact]
    public async Task Delete_with_confirm_and_proof_returns_guest_and_keeps_guest_rows()
    {
        await using var f = new Fixture();
        ProfileCoordinatorTests.Seed(f.Profiles.Current.Services, "guest-canary");
        await f.Login();
        Assert.Empty(f.Profiles.Current.Services.Homework.GetAll());
        f.Handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/account/reauthenticate" => Json(Proof("delete_account")),
            "/api/v1/account" when request.Method == HttpMethod.Delete =>
                Json(new DeleteAccountResponse("deleting", false), HttpStatusCode.Accepted),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent)
        });
        f.Vm.RequestDeleteCommand.Execute(null);
        Assert.True(f.Vm.ConfirmDelete);
        f.Vm.Proof = Password;
        await f.Vm.DeleteAccountCommand.ExecuteAsync(null);
        Assert.True(f.Vm.IsGuest);
        Assert.Null(f.Vault.Entry);
        Assert.Equal("", f.Vm.Proof);
        Assert.Equal("guest-canary", Assert.Single(f.Profiles.Current.Services.Homework.GetAll()).Text);
        Assert.Contains("отдельно", Loc.Current.T("accountDeleteConfirm"));
        Assert.False(f.Vm.ConfirmDelete);
    }

    [Fact]
    public async Task Vk_start_uses_mock_authorize_url_pkce_and_never_embeds_secrets()
    {
        await using var f = new Fixture(vk: true, yandex: true);
        await f.Vm.InitializeAsync();
        var start = new ExternalStartResponse(Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "https://example.invalid/mock/authorize?state=abc", Now.AddMinutes(10));
        string? body = null;
        f.Handler.Send = async (request, ct) =>
        {
            Assert.Equal("/api/v1/auth/external/vk/start", request.RequestUri!.AbsolutePath);
            Assert.DoesNotContain("oauth.vk.com", request.RequestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("yandex", request.RequestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            Assert.Null(request.Headers.Authorization);
            body = await request.Content!.ReadAsStringAsync(ct);
            return Json(start);
        };
        await f.Vm.StartVkCommand.ExecuteAsync(null);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("login", doc.RootElement.GetProperty("purpose").GetString());
        Assert.Equal("S256", doc.RootElement.GetProperty("nativeChallengeMethod").GetString());
        Assert.Equal(43, doc.RootElement.GetProperty("nativeChallenge").GetString()!.Length);
        Assert.Equal("windows", doc.RootElement.GetProperty("device").GetProperty("platform").GetString());
        Assert.Equal("windows", doc.RootElement.GetProperty("nativeReturn").GetProperty("kind").GetString());
        Assert.InRange(doc.RootElement.GetProperty("nativeReturn").GetProperty("port").GetInt32(), 1024, 65535);
        Assert.False(doc.RootElement.TryGetProperty("clientSecret", out _));
        Assert.False(doc.RootElement.TryGetProperty("client_secret", out _));
        Assert.DoesNotContain("client_secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(start.AuthorizeUrl, f.Vm.AuthorizeUrl);
        Assert.Equal(start.AuthorizeUrl, Assert.Single(f.Launcher.Urls));
        Assert.Contains("VK ID", f.Vm.Status);
        Assert.DoesNotContain(start.AuthorizeUrl, f.Vm.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("nativeChallenge", f.Vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Identities_list_and_unlink_require_proof_and_stay_on_account()
    {
        await using var f = new Fixture(vk: true, yandex: true);
        await f.Login();
        var identity = new ExternalIdentityResponse("vk", Now);
        f.Handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/account/identities" when request.Method == HttpMethod.Get => Json(new[] { identity }),
            "/api/v1/account/reauthenticate" => Json(Proof("unlink:vk")),
            "/api/v1/account/identities/vk" when request.Method == HttpMethod.Delete =>
                new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
        });
        await f.Vm.LoadIdentitiesCommand.ExecuteAsync(null);
        Assert.Equal("vk", Assert.Single(f.Vm.Identities).Provider);
        Assert.False(f.Vm.ShowVkLink);
        Assert.True(f.Vm.ShowYandexLink);
        f.Vm.Proof = Password;
        await f.Vm.UnlinkIdentityCommand.ExecuteAsync(identity);
        Assert.Empty(f.Vm.Identities);
        Assert.True(f.Vm.ShowVkLink);
        Assert.False(f.Vm.IsGuest);
        Assert.Equal("", f.Vm.Proof);
        Assert.Contains("Отвязать", f.Vm.Status);
    }

    private static HttpResponseMessage ExportFile()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ExportBytes) };
        response.Content.Headers.ContentType = new("application/json");
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = "zapara-export.json"
        };
        return response;
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
        public FakeLauncher Launcher { get; } = new();
        public FakeFileDialogs Dialogs { get; } = new();

        public Fixture(bool vk = false, bool yandex = false, bool recovery = false, bool registration = true)
        {
            var services = AppServices.Create(directory.Root, () => false);
            services.AllowNetwork = false;
            services.Launcher = Launcher;
            services.FileDialogs = Dialogs;
            http = new(Handler);
            client = new(http, new Uri("http://127.0.0.1/"));
            Vault = new(client.Scope.Key);
            Profiles = ProfileCoordinatorTests.Coordinator(services, client, Vault, TimeSpan.FromMilliseconds(500));
            Vm = new(Profiles, new AccountUiService(client, Vault, Profiles));
            Handler.Send = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/auth/capabilities" => Json(new AuthCapabilitiesResponse(true, vk, yandex, registration, recovery)),
                "/api/v1/auth/register" => Json(User, HttpStatusCode.Created),
                "/api/v1/auth/login" => Json(new SessionResponse(User, FamilyId, Token("za_"), Token("zr_"),
                    "Bearer", DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow.AddDays(30))),
                "/api/v1/account/devices" => Json(new DevicesResponse([new DeviceResponse(FamilyId, DeviceId,
                    "Windows", "windows", Now, Now, Now.AddDays(30), true)], null)),
                "/api/v1/account/identities" => Json(Array.Empty<ExternalIdentityResponse>()),
                _ => new HttpResponseMessage(HttpStatusCode.NoContent)
            });
        }

        public async Task Login()
        {
            await Vm.InitializeAsync();
            Vm.Username = "Test.User";
            Vm.Password = Password;
            await Vm.SubmitCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            Vm.Dispose();
            await Profiles.ExitAsync();
            await Profiles.RemoteLogouts;
            Vault.Dispose();
            client.Dispose();
            http.Dispose();
            directory.Dispose();
        }
    }
}
