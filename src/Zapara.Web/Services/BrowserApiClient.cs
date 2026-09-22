using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Sync;

namespace Zapara.Web.Services;

public sealed record BrowserCapabilities(bool Password = false, bool Vk = false, bool Yandex = false, bool Registration = false, bool Recovery = false);
public sealed record BrowserSession(bool Authenticated, UserResponse? User, Guid? FamilyId, string CsrfToken, BrowserCapabilities Capabilities)
{
    public static readonly BrowserSession Anonymous = new(false, null, null, "", new());
}
public sealed class BrowserApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed partial class BrowserApiClient(HttpClient http, BrowserStorage storage) : IAsyncDisposable
{
    private readonly SemaphoreSlim sessionGate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    public BrowserSession Session { get; private set; } = BrowserSession.Anonymous;
    public long Revision { get; private set; }
    public event Func<BrowserSession, Task>? SessionChanged;
    public bool Available { get; private set; }

    public async Task RefreshSessionAsync(CancellationToken ct = default)
    {
        if (coordinationRequested && !coordinated) await InitializeBrowserCoordinationAsync();
        await sessionGate.WaitAsync(ct);
        try
        {
            var marker = await PrepareBootstrapAsync(ct);
            var session = await ReadSessionAsync(ct);
            if (await storage.ReadAsync<bool>("preferences", "pendingLogout"))
            {
                // The last local logout must not be silently undone when a
                // previously offline browser reconnects with its HttpOnly cookie.
                if (session.Authenticated)
                {
                    if (marker is not null && ownedTransition is null) marker = await BeginOwnedTransitionAsync("transition", ct);
                    await SendBytesAsync(HttpMethod.Post, "auth/logout", null, session, ct, ignoreRevision: true);
                }
                await storage.WriteAsync("preferences", "pendingLogout", false);
                session = await ReadSessionAsync(ct);
            }
            Available = true;
            await CheckMarkerAsync(marker); BindMarker(marker);
            await SetSessionAsync(session);
            await CompleteMarkerAsync(marker);
        }
        catch (Exception error) when (error is BrowserApiException or Microsoft.JSInterop.JSException or OperationCanceledException)
        { Available = false; await AbandonMarkerAsync(); throw; }
        finally { sessionGate.Release(); }
    }

    private async Task<BrowserSession> ReadSessionAsync(CancellationToken ct)
    {
        var bytes = await SendBytesAsync(HttpMethod.Get, "session", null, Session, ct, ignoreRevision: true);
        var session = Parse<BrowserSession>(bytes);
        if (session.CsrfToken is not { Length: 43 } || (session.Authenticated && (session.User is null || session.FamilyId is null || session.FamilyId == Guid.Empty)))
            throw Failure(502, "invalid_response");
        return session;
    }

    public async Task<BrowserSession> SignInAsync(string username, string password, string? deviceName = null, CancellationToken ct = default)
    {
        if (Session.CsrfToken.Length == 0) await RefreshSessionAsync(ct);
        await sessionGate.WaitAsync(ct);
        SessionMarker? marker = null;
        try
        {
            marker = await BeginOwnedTransitionAsync("transition", ct);
            var bytes = await SendBytesAsync(HttpMethod.Post, "auth/login", new { username, password, deviceName = deviceName ?? "Браузер Запары" }, Session, ct);
            var session = Parse<BrowserSession>(bytes);
            if (!session.Authenticated || session.User is null || session.FamilyId is null || session.FamilyId == Guid.Empty || session.CsrfToken is not { Length: 43 }) throw Failure(502, "invalid_response");
            await storage.WriteAsync("preferences", "pendingLogout", false);
            Available = true;
            await CheckMarkerAsync(marker); BindMarker(marker);
            await SetSessionAsync(session);
            await CompleteMarkerAsync(marker);
            return session;
        }
        catch { await RecoverFailedTransitionAsync(marker); throw; }
        finally { sessionGate.Release(); }
    }

    public async Task RegisterAsync(string username, string password, string? displayName = null, CancellationToken ct = default)
    {
        _ = await SendAsync<UserResponse>(HttpMethod.Post, "auth/register", new RegisterRequest(username, password, displayName), ct: ct);
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await sessionGate.WaitAsync(ct);
        SessionMarker? marker = null;
        try
        {
            var previous = Session;
            marker = await BeginOwnedTransitionAsync("transition", ct);
            await storage.WriteAsync("preferences", "pendingLogout", true);
            try
            {
                if (previous.Authenticated) await SendBytesAsync(HttpMethod.Post, "auth/logout", null, previous, ct);
                await storage.WriteAsync("preferences", "pendingLogout", false);
                var session = await ReadSessionAsync(ct);
                await CheckMarkerAsync(marker); BindMarker(marker); Available = true;
                await SetSessionAsync(session); await CompleteMarkerAsync(marker);
            }
            catch (BrowserApiException e) when (e.Code != "account_changed")
            {
                await CheckMarkerAsync(marker); BindMarker(marker); Available = false;
                await SetSessionAsync(BrowserSession.Anonymous); await CompleteMarkerAsync(marker);
                throw new BrowserApiException(0, "logout_pending", "Вы вышли на этом устройстве. Серверная сессия завершится после восстановления соединения.");
            }
        }
        catch { await RecoverFailedTransitionAsync(marker); throw; }
        finally { sessionGate.Release(); }
    }

    public Task<T> GetAsync<T>(string path, CancellationToken ct = default) => SendAsync<T>(HttpMethod.Get, path, ct: ct);

    public async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, Guid? expectedFamily = null, CancellationToken ct = default)
    {
        ValidatePath(path);
        if (method != HttpMethod.Get && Session.CsrfToken.Length == 0) await RefreshSessionAsync(ct);
        var session = Capture(expectedFamily);
        var bytes = ChangesSession(method, path) ? await SendSessionMutationAsync(method, path, body, expectedFamily, ct)
            : await SendBytesAsync(method, path, body, session, ct);
        if (typeof(T).Namespace == typeof(SyncMetadata).Namespace)
        {
            try { return SyncJson.Parse<T>(bytes); }
            catch (ArgumentException) { throw Failure(502, "invalid_response"); }
        }
        return Parse<T>(bytes);
    }

    public async Task SendAsync(HttpMethod method, string path, object? body = null, Guid? expectedFamily = null, CancellationToken ct = default)
    {
        ValidatePath(path);
        if (method != HttpMethod.Get && Session.CsrfToken.Length == 0) await RefreshSessionAsync(ct);
        if (ChangesSession(method, path)) _ = await SendSessionMutationAsync(method, path, body, expectedFamily, ct);
        else _ = await SendBytesAsync(method, path, body, Capture(expectedFamily), ct);
    }

    public Task<byte[]> DownloadAsync(string path, CancellationToken ct = default) =>
        SendBytesAsync(HttpMethod.Get, path, null, Session, ct, maximumBytes: 16 * 1024 * 1024);

    private BrowserSession Capture(Guid? expected)
    {
        if (expected is not null && Session.FamilyId != expected) throw Failure(409, "account_changed");
        return Session;
    }

    private async Task SetSessionAsync(BrowserSession value)
    {
        var changed = Session.Authenticated != value.Authenticated || Session.FamilyId != value.FamilyId
            || Session.User != value.User || Session.Capabilities != value.Capabilities;
        var generationChanged = coordinated && ConfirmedSessionGeneration != publishedSessionGeneration;
        Session = value;
        if (!changed && (sessionNotificationDepth > 0 || !sessionPublishPending && !generationChanged)) return;
        var revision = changed || generationChanged ? ++Revision : Revision;
        sessionPublishPending = true;
        if (coordinated) SetTransitioning(true);
        Interlocked.Increment(ref sessionNotificationDepth);
        // Callbacks may bootstrap or make API requests. Never hold the session semaphore across them.
        sessionGate.Release();
        try
        {
            if (SessionChanged is { } handlers)
                foreach (var handler in handlers.GetInvocationList().Cast<Func<BrowserSession, Task>>())
                {
                    if (Revision != revision) break;
                    await handler(value);
                }
            if (Revision == revision) { sessionPublishPending = false; publishedSessionGeneration = ConfirmedSessionGeneration; }
        }
        finally { Interlocked.Decrement(ref sessionNotificationDepth); await sessionGate.WaitAsync(); }
    }

    private async Task<byte[]> SendBytesAsync(HttpMethod method, string path, object? body, BrowserSession session, CancellationToken ct,
        bool ignoreRevision = false, int maximumBytes = 8 * 1024 * 1024)
    {
        ValidatePath(path);
        var revision = Revision;
        var marker = await RequestMarkerAsync(path == "session" || ignoreRevision, method, path);
        using var request = new HttpRequestMessage(method, "/web-api/" + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (session.FamilyId is { } family) request.Headers.Add("X-Zapara-Family", family.ToString("D"));
        if (method != HttpMethod.Get) request.Headers.Add("X-Zapara-CSRF", session.CsrfToken);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), SyncJson.CreateOptions()));
            request.Content.Headers.ContentType = new("application/json");
        }
        if (OperatingSystem.IsBrowser())
        {
            request.SetBrowserRequestCredentials(BrowserRequestCredentials.SameOrigin);
            request.SetBrowserRequestOption("redirect", "error");
        }
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await CheckMarkerAsync(marker);
            if (!ignoreRevision && revision != Revision) throw Failure(409, "account_changed");
            if (response.Content.Headers.ContentLength > maximumBytes) throw Failure(502, "invalid_response");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maximumBytes - (int)output.Length + 1)), ct);
                if (count == 0) break;
                if (output.Length + count > maximumBytes) throw Failure(502, "invalid_response");
                output.Write(buffer, 0, count);
            }
            if (!ignoreRevision && revision != Revision) throw Failure(409, "account_changed");
            await CheckMarkerAsync(marker);
            var bytes = output.ToArray();
            if (!response.IsSuccessStatusCode)
            {
                // Mutation conflicts carry an authoritative typed outcome, not a
                // generic failure. Preserve it for explicit conflict resolution.
                if (path == "sync/mutations" && response.StatusCode == HttpStatusCode.Conflict)
                {
                    try { if (SyncJson.Parse<SyncMutationResult>(bytes).Status == 409) return bytes; }
                    catch (ArgumentException) { }
                }
                string code = "request_failed";
                try { using var problem = JsonDocument.Parse(bytes); if (problem.RootElement.ValueKind == JsonValueKind.Object && problem.RootElement.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: < 100 } parsed) code = parsed; } catch (JsonException) { }
                throw Failure((int)response.StatusCode, code);
            }
            return bytes;
        }
        catch (HttpRequestException) { throw Failure(0, "network_unavailable"); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw Failure(0, "network_unavailable"); }
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.StartsWith('/') || path.Contains('\\') || path.Contains('#')
            || path.Split('?')[0].Split('/').Any(p => Uri.UnescapeDataString(p) is "." or "..")
            || Uri.TryCreate(path, UriKind.Absolute, out _)) throw Failure(400, "invalid_request");
    }

    private static T Parse<T>(byte[] bytes)
    {
        try { return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw Failure(502, "invalid_response"); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { throw Failure(502, "invalid_response"); }
    }

    private static BrowserApiException Failure(int status, string code) => new(status, code, code switch
    {
        "network_unavailable" => "Нет связи с сервером. Сохранённые данные доступны на устройстве.",
        "account_changed" or "browser_session_changed" or "session_changed" => "Аккаунт изменился в другой вкладке. Обновите данные и повторите действие.",
        "session_transition" => "В другой вкладке идёт переключение аккаунта. Дождитесь завершения или повторите проверку.",
        "session_coordination_unavailable" => "Браузер не поддерживает безопасное переключение аккаунтов между вкладками.",
        "invalid_credentials" => "Проверьте имя пользователя и пароль.",
        "username_taken" => "Это имя пользователя уже занято.",
        "invalid_session" => "Сессия завершена. Войдите ещё раз.",
        "csrf_invalid" => "Состояние входа изменилось. Обновите страницу и повторите действие.",
        "registration_unavailable" => "Регистрация на сервере пока отключена.",
        "provider_unavailable" => "Этот способ входа пока не настроен.",
        "recovery_unavailable" => "Восстановление пароля пока недоступно.",
        "db_unavailable" => "Сервер временно недоступен. Попробуйте позже.",
        "rate_limited" => "Слишком много попыток. Подождите немного.",
        "already_voted" => "Ваш голос уже принят.",
        "poll_closed" => "Голосование завершено.",
        "forbidden" => "Недостаточно прав для этого действия.",
        "invalid_response" => "Сервер вернул неподходящие данные. Сохранённая копия не изменена.",
        _ when status == 401 => "Войдите в аккаунт, чтобы продолжить.",
        _ when status == 403 => "Недостаточно прав для этого действия.",
        _ when status == 400 => "Проверьте заполненные поля.",
        _ => "Не удалось выполнить действие. Попробуйте ещё раз."
    });
}
