using Microsoft.JSInterop;

namespace Zapara.Web.Services;

public sealed record PushCapabilities(bool Available, string? PublicKey, string? Reason);
public sealed record PushSubscriptionInfo(Guid SubscriptionId, bool Enabled, string TimeZone, string?[] Times);
public sealed record PushBinding(Guid FamilyId, Guid SubscriptionId);
public sealed record PushKeys(string P256dh, string Auth);
public sealed record PushPayload(string Endpoint, PushKeys Keys, bool Enabled, string TimeZone);
public sealed record BrowserSubscribeResult(bool Created, PushPayload Value);
public sealed record PushTestResult(string Status);
public sealed record BrowserPushStatus(bool Secure = false, bool Supported = false, string Permission = "unsupported",
    bool Ios = false, bool Installed = false, string TimeZone = "Europe/Moscow", bool Registered = false,
    bool Subscribed = false, bool UpdateAvailable = false);

public sealed class BrowserNotifications(IJSRuntime js, BrowserApiClient api, BrowserStorage storage, WebAppState state) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Task<IJSObjectReference>? module;
    private DotNetObjectReference<BrowserNotifications>? reference;
    private PushBinding? binding;
    private bool watching;
    private bool updateCheckNotice;
    private long updateStatusRevision;
    public event Action? Changed;
    public BrowserPushStatus Browser { get; private set; } = new();
    public PushCapabilities Capabilities { get; private set; } = new(false, null, null);
    public PushSubscriptionInfo? Subscription { get; private set; }
    public bool Enabled => Browser.Subscribed && Subscription is { Enabled: true } && binding?.FamilyId == api.Session.FamilyId;
    public bool Authenticated => api.Session.Authenticated && api.Session.FamilyId is not null;
    public bool Ready { get; private set; }
    public bool Busy { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public Task InitializeAsync(CancellationToken ct = default) => RefreshAsync(ct);
    public Task RefreshAsync(CancellationToken ct = default) => RunAsync(async () =>
    {
        await EnsureModuleAsync();
        await ReconcileCoreAsync(ct);
        Ready = true;
    }, ct);

    // Called by the account coordinator after a session family changes; never silently rebind.
    public Task SessionChangedAsync(CancellationToken ct = default) => RefreshAsync(ct);

    public Task EnableAsync(CancellationToken ct = default) => RunAsync(async () =>
    {
        var family = Family();
        if (!Ready || !Capabilities.Available || string.IsNullOrEmpty(Capabilities.PublicKey))
            throw new InvalidOperationException("Отправка уведомлений на сервере пока не настроена.");
        var bridge = await EnsureModuleAsync();
        BrowserSubscribeResult? created = null;
        PushSubscriptionInfo? registered = null;
        try
        {
            // Module and capabilities are loaded before enabling the button: preserve the user's gesture.
            created = await bridge.InvokeAsync<BrowserSubscribeResult>("subscribe", ct, Capabilities.PublicKey);
            EnsureFamily(family);
            registered = await api.SendAsync<PushSubscriptionInfo>(HttpMethod.Post, "notifications/subscriptions", created.Value, family, ct);
            EnsureFamily(family);
            if (!registered.Enabled || registered.SubscriptionId == Guid.Empty) throw new InvalidDataException();
            var saved = new PushBinding(family, registered.SubscriptionId);
            await storage.WriteAsync("preferences", "notifications-binding", saved);
            binding = saved; Subscription = registered;
            Browser = await bridge.InvokeAsync<BrowserPushStatus>("inspect", ct);
            Message = "Уведомления включены для этого браузера.";
        }
        catch
        {
            if (registered is not null && api.Session.FamilyId == family)
            {
                try { await api.SendAsync(HttpMethod.Delete, "notifications/subscriptions/" + registered.SubscriptionId, expectedFamily: family, ct: ct); }
                catch (Exception e) when (e is BrowserApiException or HttpRequestException or TaskCanceledException) { }
            }
            if (created?.Created == true)
            {
                try { await bridge.InvokeAsync<bool>("unsubscribe"); }
                catch (JSException) { }
            }
            throw;
        }
    }, ct);

    public Task DisableAsync(CancellationToken ct = default) => RunAsync(() => DisableCoreAsync(ct), ct);

    // Local unsubscribe is attempted even when the server is unavailable. Sign-out must still revoke the family.
    public Task PrepareLogoutAsync(CancellationToken ct = default) => DisableAsync(ct);

    public Task TestAsync(CancellationToken ct = default) => RunAsync(async () =>
    {
        var family = Family();
        if (!Enabled || Subscription is null) throw new InvalidOperationException("Сначала включите уведомления в этом браузере.");
        var result = await api.SendAsync<PushTestResult>(HttpMethod.Post, "notifications/test", new { subscriptionId = Subscription.SubscriptionId }, family, ct);
        EnsureFamily(family);
        if (result.Status != "accepted") throw new InvalidOperationException("Служба доставки не приняла проверочное уведомление.");
        Message = "Проверочное уведомление передано службе доставки.";
    }, ct);

    public Task CheckUpdateAsync(CancellationToken ct = default) => RunAsync(async () =>
    {
        var bridge = await EnsureModuleAsync();
        var revision = updateStatusRevision;
        var available = await bridge.InvokeAsync<bool>("checkUpdate", ct);
        // A watch event delivered during the request is newer than its returned snapshot.
        if (revision == updateStatusRevision) Browser = Browser with { UpdateAvailable = available };
        updateCheckNotice = true;
        SetUpdateCheckNotice();
    }, ct);

    public Task ActivateUpdateAsync(CancellationToken ct = default) => RunAsync(async () =>
    {
        if (!state.StorageAvailable) throw new InvalidOperationException("Сначала восстановите доступ к хранилищу, чтобы не потерять изменения при перезагрузке.");
        await state.WaitForStorageAsync(ct);
        await (await EnsureModuleAsync()).InvokeVoidAsync("activateUpdate", ct);
        Message = "Обновление применяется. Приложение перезагрузится после активации.";
    }, ct);

    [JSInvokable] public Task UpdateAvailableChanged(bool available)
    {
        updateStatusRevision++;
        Browser = Browser with { UpdateAvailable = available };
        if (updateCheckNotice) SetUpdateCheckNotice();
        Changed?.Invoke(); return Task.CompletedTask;
    }

    private void SetUpdateCheckNotice() => Message = Browser.UpdateAvailable
        ? "Новая версия готова к установке."
        : "Готового обновления пока нет. Если версия загружается, кнопка появится после загрузки.";
    [JSInvokable] public Task UpdateActivationFailed()
    {
        Error = "Обновление не завершилось. Проверьте соединение и повторите попытку.";
        Changed?.Invoke(); return Task.CompletedTask;
    }

    private async Task<IJSObjectReference> EnsureModuleAsync()
    {
        var bridge = await (module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/notifications.js").AsTask());
        if (!watching)
        {
            reference ??= DotNetObjectReference.Create(this);
            watching = await bridge.InvokeAsync<bool>("watch", reference);
        }
        return bridge;
    }

    private async Task ReconcileCoreAsync(CancellationToken ct)
    {
        var bridge = await EnsureModuleAsync();
        binding = await storage.ReadAsync<PushBinding>("preferences", "notifications-binding");
        if (api.Available && binding is not null && (!Authenticated || binding.FamilyId != api.Session.FamilyId))
        {
            await bridge.InvokeAsync<bool>("unsubscribe", ct);
            await storage.WriteAsync<PushBinding?>("preferences", "notifications-binding", null);
            binding = null;
        }
        Browser = await bridge.InvokeAsync<BrowserPushStatus>("inspect", ct);
        if (api.Available && binding is null && Browser.Subscribed)
        {
            // A cleared local binding cannot establish which account owns a surviving browser endpoint.
            await bridge.InvokeAsync<bool>("unsubscribe", ct);
            Browser = Browser with { Subscribed = false };
        }
        Capabilities = await api.GetAsync<PushCapabilities>("notifications/capabilities", ct);
        Subscription = null;
        if (Authenticated && binding is not null)
        {
            var family = Family();
            var subscriptions = await api.GetAsync<PushSubscriptionInfo[]>("notifications/subscriptions", ct);
            EnsureFamily(family);
            Subscription = subscriptions.FirstOrDefault(item => item.SubscriptionId == binding.SubscriptionId && item.Enabled);
        }
    }

    private async Task DisableCoreAsync(CancellationToken ct)
    {
        var bridge = await EnsureModuleAsync();
        binding ??= await storage.ReadAsync<PushBinding>("preferences", "notifications-binding");
        Exception? serverError = null;
        if (Authenticated)
        {
            var family = Family();
            try
            {
                var subscriptions = await api.GetAsync<PushSubscriptionInfo[]>("notifications/subscriptions", ct);
                EnsureFamily(family);
                foreach (var item in subscriptions)
                    await api.SendAsync(HttpMethod.Delete, "notifications/subscriptions/" + item.SubscriptionId, expectedFamily: family, ct: ct);
            }
            catch (Exception e) when (e is BrowserApiException or HttpRequestException or TaskCanceledException) { serverError = e; }
        }
        await bridge.InvokeAsync<bool>("unsubscribe", ct);
        Browser = Browser with { Subscribed = false }; Subscription = null;
        if (serverError is null)
        {
            binding = null; await storage.WriteAsync<PushBinding?>("preferences", "notifications-binding", null);
            Message = "Уведомления отключены в этом браузере.";
        }
        else Message = "Уведомления отключены в браузере. Подтвердить удаление на сервере не удалось; повторите при подключении к сети.";
    }

    private Guid Family() => Authenticated && api.Session.FamilyId is { } family ? family
        : throw new InvalidOperationException("Для уведомлений войдите в аккаунт.");
    private void EnsureFamily(Guid family)
    {
        if (!Authenticated || api.Session.FamilyId != family) throw new InvalidOperationException("Аккаунт изменился. Повторите действие в текущем аккаунте.");
    }

    private async Task RunAsync(Func<Task> action, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        Busy = true; Error = null; Message = null; updateCheckNotice = false; Changed?.Invoke();
        try { await action(); }
        catch (BrowserApiException e) { Error = e.Message; }
        catch (InvalidOperationException e) { Error = e.Message; }
        catch (JSException)
        {
            if (module?.IsCompletedSuccessfully == true)
            {
                try { Browser = await module.Result.InvokeAsync<BrowserPushStatus>("inspect"); } catch (JSException) { }
            }
            Error = Browser.Permission == "denied" ? "Уведомления запрещены. Разрешите их в настройках браузера и повторите проверку."
                : "Не удалось завершить действие в браузере. Проверьте разрешения, подключение и доступность хранилища.";
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException)
        { Error = "Не удалось завершить действие. Проверьте разрешения браузера, подключение и доступность хранилища."; }
        catch (OperationCanceledException) { Error = "Операция отменена."; }
        finally { Busy = false; gate.Release(); Changed?.Invoke(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null && module.IsCompletedSuccessfully)
        {
            try { await module.Result.InvokeVoidAsync("dispose"); await module.Result.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        reference?.Dispose();
    }
}
