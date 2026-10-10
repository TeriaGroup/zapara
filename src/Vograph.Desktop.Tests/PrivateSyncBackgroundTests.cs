using System.Net;
using System.Net.Http.Headers;
using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;
using Zapara.Contracts.Sync;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.PrivateSyncOutboxTests;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncBackgroundTests
{
    private static readonly Guid Epoch = Guid.Parse("abababab-abab-4bab-8bab-abababababab");
    private static readonly Guid ManifestId = Guid.Parse("cdcdcdcd-cdcd-4dcd-8dcd-cdcdcdcdcdcd");
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Access = Token("za_");

    [Fact]
    public async Task Local_commit_wakes_background_push_before_the_long_poll()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var calls = new List<string>();
        using var http = new HttpClient(new Script(async (request, ct) => await Respond(request, calls, ct)));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/sync-test/"));
        app.PrivateSync!.PollInterval = TimeSpan.FromMinutes(5);
        app.PrivateSync.Attach(client, _ => Task.FromResult(Access), background: true);
        await Until(() => { lock (calls) return calls.Count(path => path == "changes") >= 1; });
        app.Homework.AddHomework("лек ИСТОРИЯ", "после входа", 1, new DateTime(2026, 9, 29));
        // #95: «сервер получил mutations» ещё не значит «клиент обработал ответ». Фейковый сервер пишет вызов до ответа,
        // а запись уходит из outbox только в ApplyPush — после ответа, CoreGate и транзакции. Ждём само условие.
        // Окно 10 с при PollInterval 5 мин: если отправку будит не локальная запись, тест по-прежнему падает.
        await UntilOutboxEmpty(app, TimeSpan.FromSeconds(10));
        Assert.Empty(app.Outbox.Pending());
        lock (calls) Assert.Contains("mutations", calls);
    }

    [Fact]
    public async Task Manual_cycle_pulls_then_pushes_then_pulls_again()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        app.Homework.AddHomework("лек ИСТОРИЯ", "черновик", 1, new DateTime(2026, 9, 29));
        var calls = new List<string>();
        using var http = new HttpClient(new Script(async (request, ct) => await Respond(request, calls, ct)));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/sync-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access));
        await app.PrivateSync.SyncNowAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["resync", "changes", "mutations", "changes"], calls);
        Assert.Empty(app.Outbox.Pending());
        Assert.NotNull(app.PrivateSync.Health.LastSuccessAt);
        Assert.Null(app.PrivateSync.Health.LastFailure);
    }

    [Fact]
    public async Task Profile_retirement_during_manual_pull_prevents_late_push()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        app.Homework.AddHomework("лек ИСТОРИЯ", "черновик", 1, new DateTime(2026, 9, 29));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutations = 0;
        using var http = new HttpClient(new Script(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/resync", StringComparison.Ordinal))
            {
                started.TrySetResult();
                await release.Task;
                return Json(new SyncResyncManifest(ManifestId, Epoch, 0, Now, Now.AddMinutes(10), 0));
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/mutations", StringComparison.Ordinal)) mutations++;
            return await Respond(request, [], ct);
        }));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/sync-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access));
        var cycle = app.PrivateSync.SyncNowAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        app.Work.Suspend();
        release.SetResult();
        await cycle.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, mutations);
        Assert.Single(app.Outbox.Pending());
    }

    [Fact]
    public async Task Token_failure_backs_off_and_explicit_wake_recovers()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var attempts = 0;
        var available = false;
        var calls = new List<string>();
        using var http = new HttpClient(new Script(async (request, ct) => await Respond(request, calls, ct)));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/sync-test/"));
        app.PrivateSync!.PollInterval = TimeSpan.FromMinutes(5);
        app.PrivateSync.RetryInterval = TimeSpan.FromSeconds(2);
        app.PrivateSync.Attach(client, _ =>
        {
            Interlocked.Increment(ref attempts);
            return Volatile.Read(ref available) ? Task.FromResult(Access)
                : Task.FromException<string>(new AccountClientException(AccountClientFailure.InvalidSession));
        }, background: true);
        await Until(() => Volatile.Read(ref attempts) > 0);
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(1, Volatile.Read(ref attempts));
        Assert.Contains("повторный вход", app.PrivateSync.Health.LastFailure);
        available = true;
        app.PrivateSync.Wake();
        await Until(() => app.PrivateSync.Health.LastSuccessAt is not null && app.PrivateSync.Health.LastFailure is null);
        Assert.True(Volatile.Read(ref attempts) >= 2);
    }

    [AvaloniaFact]
    public async Task Open_data_panel_tracks_pending_failure_and_success_after_manual_retry()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var available = false;
        var calls = new List<string>();
        using var http = new HttpClient(new Script(async (request, ct) =>
            available ? await Respond(request, calls, ct)
                : Json(new SyncError(503, "db_unavailable"), HttpStatusCode.ServiceUnavailable)));
        using var client = new PrivateSyncHttpClient(http, new Uri("http://127.0.0.1/sync-test/"));
        app.PrivateSync!.Attach(client, _ => Task.FromResult(Access));
        var vm = new SettingsViewModel(app, new ShellViewModel(app));
        vm.OpenPanelCommand.Execute("data");
        app.Homework.AddHomework("лек ИСТОРИЯ", "локально", 1, new DateTime(2026, 9, 29));
        await Until(() => vm.DataSummary.Contains("ожидают отправки: 1", StringComparison.Ordinal));
        await vm.SyncDataCommand.ExecuteAsync(null);
        Assert.Contains("недоступны", vm.DataSummary);
        Assert.Single(app.Outbox.Pending());
        available = true;
        Assert.True(vm.CanSyncData, vm.DataSummary);
        await vm.SyncDataCommand.ExecuteAsync(null);
        Assert.Empty(app.Outbox.Pending());
        Assert.Contains("ожидают отправки: 0", vm.DataSummary);
        Assert.Contains("Последний успешный обмен", vm.DataSummary);
        vm.Detach();
    }

    private static async Task<HttpResponseMessage> Respond(HttpRequestMessage request, List<string> calls, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/resync", StringComparison.Ordinal))
        {
            lock (calls) calls.Add("resync");
            return Json(new SyncResyncManifest(ManifestId, Epoch, 0, Now, Now.AddMinutes(10), 0));
        }
        if (path.EndsWith("/changes", StringComparison.Ordinal))
        {
            lock (calls) calls.Add("changes");
            return Json(new SyncChangesPage(new SyncMetadata(Epoch, 0, 0), 0, 0, false, []));
        }
        if (path.EndsWith("/mutations", StringComparison.Ordinal))
        {
            lock (calls) calls.Add("mutations");
            var mutation = SyncJson.Parse<SyncMutation>(await request.Content!.ReadAsByteArrayAsync(ct));
            var record = new SyncRecord(mutation.EntityType, mutation.EntityId, 1, false, Now, mutation.Value);
            return Json(new SyncMutationResult(200, "applied", new SyncMetadata(Epoch, 1, 0), record));
        }
        throw new InvalidOperationException(path);
    }

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(SyncJson.Serialize(value)) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }

    /// <summary>
    /// Ждёт пустого outbox без опроса по таймеру: координатор поднимает HealthChanged после каждого
    /// применённого ответа (ReportSuccess/ReportFailure) и в начале/конце цикла. Обработчик только будит
    /// ожидание; Pending() читается здесь, вне CoreGate координатора.
    /// </summary>
    private static async Task UntilOutboxEmpty(AppServices app, TimeSpan timeout)
    {
        using var wake = new SemaphoreSlim(0);
        void Wake() => wake.Release();
        app.PrivateSync!.HealthChanged += Wake;
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            while (app.Outbox.Pending().Count > 0)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero || !await wake.WaitAsync(left))
                    Assert.Fail($"outbox не опустел за {timeout.TotalSeconds:0} с: {app.Outbox.Pending().Count} записей");
            }
        }
        finally { app.PrivateSync!.HealthChanged -= Wake; }
    }

    private static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) await Task.Delay(20, timeout.Token);
    }

    private sealed class Script(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
