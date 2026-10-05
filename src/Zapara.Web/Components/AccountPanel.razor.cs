using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Zapara.Contracts.Accounts;
using Zapara.Web.Services;

namespace Zapara.Web.Components;

public partial class AccountPanel
{
    [Inject] private BrowserApiClient Api { get; set; } = default!;
    [Inject] private BrowserAccountService Accounts { get; set; } = default!;
    [Inject] private BrowserNotifications Notifications { get; set; } = default!;
    [Inject] private WebAppState State { get; set; } = default!;
    private string? dialog, error, status;
    private string username = "", displayName = "", password = "", passwordAgain = "", currentPassword = "", newPassword = "";
    private string email = "", verificationCode = "", resetCode = "", confirmationText = "", selectedProvider = "";
    private bool busy, disposed, deleteAcknowledged, pendingOAuth;
    private Guid? observedFamily, dialogFamily, operationFamily;
    private DeviceResponse? selectedDevice;
    private CancellationTokenSource operation = new();
    private string AccountLabel => Api.Session.User is { } user ? user.DisplayName is { Length: > 0 } name ? name + " · " + user.Username : user.Username : State.AccountName;
    private string ProofPurpose => dialog switch { "link" => "link:" + selectedProvider, "unlink" => "unlink:" + selectedProvider, _ => "" };
    private bool CanDelete => !busy && deleteAcknowledged && confirmationText == Api.Session.User?.Username && Accounts.HasProof("delete_account");
    private string ExportStatus => Accounts.Export?.Status switch { "ready" => "Копия данных готова.", "queued" or "processing" => "Сервер готовит копию данных…", "failed" => "Сервер не смог подготовить копию.", _ => "Проверяем состояние копии данных." };
    private string Title => dialog switch
    {
        "login" => "Вход в аккаунт", "register" => "Новый аккаунт", "profile" => "Профиль", "methods" => "Способы входа",
        "change-password" => "Смена пароля", "set-password" => "Создание пароля", "devices" => "Устройства и сессии",
        "email" => "Почта восстановления", "reset" => "Восстановление доступа", "export" => "Данные аккаунта",
        "delete" => "Удаление аккаунта", "logout" => "Выход из аккаунта", "revoke-all" => "Завершение всех сессий",
        "revoke-device" => "Завершение сессии", "link" => "Подключение " + ProviderLabel(selectedProvider),
        "unlink" => "Отключение " + ProviderLabel(selectedProvider), _ => "Аккаунт"
    };
    protected override void OnInitialized()
    {
        observedFamily = Api.Session.FamilyId;
        Api.SessionChanged += SessionChanged;
        Accounts.OAuthResumed += OAuthResumed;
        State.Changed += Changed;
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Run(async ct =>
        {
            await ResumeCore(ct);
            if (Api.Session.Authenticated && !Api.Transitioning) await Accounts.RefreshAsync(ct);
        });
    }
    private Task SessionChanged(BrowserSession session)
    {
        if (observedFamily != session.FamilyId)
        {
            observedFamily = session.FamilyId; dialog = null; dialogFamily = null; selectedDevice = null;
            ClearSecrets(); Accounts.ClearProof(); confirmationText = ""; deleteAcknowledged = false;
        }
        Changed(); return Task.CompletedTask;
    }
    private void Changed() { if (!disposed) _ = InvokeAsync(StateHasChanged); }
    private async Task OpenAsync(string value)
    {
        if (busy) return;
        operation.Cancel(); operation.Dispose(); operation = new();
        ClearSecrets(); Accounts.ClearProof(); error = null; status = null;
        dialog = value; dialogFamily = Api.Session.FamilyId;
        displayName = Api.Session.User?.DisplayName ?? ""; confirmationText = ""; deleteAcknowledged = false;
        if (Api.Session.Authenticated)
            await Run(async ct => { await Accounts.RefreshAsync(ct); if (value == "devices") await Accounts.LoadDevicesAsync(ct: ct); });
    }
    private void Close()
    {
        operation.Cancel(); operation.Dispose(); operation = new();
        dialog = null; dialogFamily = null; ClearSecrets(); Accounts.ClearProof(); error = null;
    }
    private Task Reconnect() => Run(async ct => { await Api.RefreshSessionAsync(ct); if (Api.Session.Authenticated) await Accounts.RefreshAsync(ct); await ResumeCore(ct); status ??= "Состояние подключения обновлено."; });
    private Task SubmitLogin() => Run(async ct =>
    {
        var login = AccountValidation.Username(username.Trim());
        var secret = AccountValidation.Password(password);
        if (dialog == "register")
        {
            Match(secret, passwordAgain);
            await Api.RegisterAsync(login, secret, string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(), ct);
        }
        await Api.SignInAsync(login, secret, ct: ct);
        await Accounts.RefreshAsync(ct);
        dialog = null; status = "Вход выполнен. Гостевые записи можно перенести отдельно.";
    });
    private Task SaveProfile() => Run(async ct => { await Accounts.UpdateNameAsync(displayName, ct); dialog = null; status = "Имя обновлено."; });
    private Task SavePassword() => Run(async ct =>
    {
        var value = AccountValidation.Password(newPassword); Match(value, passwordAgain);
        if (dialog == "change-password")
        {
            await Accounts.ChangePasswordAsync(currentPassword, value, ct);
            await Notifications.PrepareLogoutAsync(ct);
            status = "Пароль изменён. Войдите с новым паролем.";
        }
        else { await Accounts.SetPasswordAsync(value, ct); await Notifications.PrepareLogoutAsync(ct); status = "Пароль задан. Войдите по имени пользователя с новым паролем."; }
        dialog = null;
    });
    private Task ProviderLogin(string provider) => Run(ct => Accounts.StartOAuthAsync(provider, ct: ct));
    private async Task ProviderAction(string action, string provider) { selectedProvider = provider; await OpenAsync(action); }
    private Task CompleteProviderAction() => Run(async ct =>
    {
        if (dialog == "link") await Accounts.StartOAuthAsync(selectedProvider, "link", ct: ct);
        else { await Accounts.UnlinkAsync(selectedProvider, ct); dialog = "methods"; status = "Способ входа отключён."; }
    });
    private Task SendRecoveryEmail() => Run(async ct => { await Accounts.StartRecoveryEmailAsync(email.Trim(), ct); status = "Код отправлен. Введите его ниже, чтобы подтвердить почту."; });
    private Task ConfirmEmail() => Run(async ct => { await Accounts.ConfirmRecoveryEmailAsync(verificationCode, ct); status = "Почта подтверждена."; });
    private Task RequestReset() => Run(async ct => { await Accounts.RequestPasswordResetAsync(username, ct); status = "Если восстановление доступно для этого аккаунта, код придёт на подтверждённую почту."; });
    private Task ConfirmReset() => Run(async ct =>
    {
        Match(newPassword, passwordAgain); await Accounts.ConfirmPasswordResetAsync(resetCode, newPassword, ct);
        await Api.RefreshSessionAsync(ct); dialog = "login"; dialogFamily = null;
        status = "Доступ восстановлен. Введите новый пароль, чтобы войти.";
    });
    private Task MoreDevices() => Run(ct => Accounts.LoadDevicesAsync(true, ct));
    private Task ReloadDevices() => Run(ct => Accounts.LoadDevicesAsync(ct: ct));
    private void SelectDevice(DeviceResponse device) { if (busy) return; selectedDevice = device; dialog = "revoke-device"; error = null; }
    private Task RevokeDevice() => Run(async ct =>
    {
        var device = selectedDevice ?? throw new InvalidOperationException("Выберите устройство.");
        if (device.IsCurrent) { await Notifications.PrepareLogoutAsync(ct); EnsureOperation(); }
        await Accounts.RevokeDeviceAsync(device.FamilyId, ct);
        dialog = Api.Session.Authenticated ? "devices" : null; status = "Сессия завершена.";
    });
    private Task RevokeAll() => Run(async ct =>
    {
        await Notifications.PrepareLogoutAsync(ct); EnsureOperation();
        await Accounts.RevokeAllAsync(ct); dialog = null; status = "Все сессии завершены.";
    });
    private Task Logout() => Run(async ct =>
    {
        await Notifications.PrepareLogoutAsync(ct); EnsureOperation();
        try { await Api.SignOutAsync(ct); status = "Вы вышли из аккаунта."; }
        catch (BrowserApiException exception) when (exception.Code == "logout_pending") { status = exception.Message; }
        dialog = null;
    });
    private Task CreateExport() => Run(async ct => { await Accounts.RequestExportAsync(ct); await Accounts.WaitExportAsync(ct); });
    private Task PollExport() => Run(async ct => { await Accounts.RefreshExportAsync(ct); await Accounts.WaitExportAsync(ct); });
    private Task DownloadExport() => Run(async ct => { await Accounts.DownloadExportAsync(ct); status = "Копия данных передана браузеру для сохранения."; });
    private void NewExport() { if (!busy) { Accounts.NewExport(); error = null; status = null; } }
    private Task DeleteAccount() => Run(async ct =>
    {
        if (!deleteAcknowledged || confirmationText != Api.Session.User?.Username) throw new InvalidOperationException("Подтвердите удаление аккаунта.");
        await Notifications.PrepareLogoutAsync(ct); EnsureOperation();
        await Accounts.DeleteAccountAsync(ct); dialog = null; status = "Аккаунт удалён. Открыт гостевой профиль.";
    });
    private Task ResumeOAuth() => Run(ResumeCore);
    private Task CancelOAuth() => Run(async ct => { await Accounts.CancelOAuthAsync(ct); pendingOAuth = false; status = "Вход отменён."; });
    private async Task ResumeCore(CancellationToken ct)
    {
        var result = Accounts.TakeOAuthResume() ?? await Accounts.ResumeOAuthAsync(ct);
        if (result?.Completed == true) result = Accounts.TakeOAuthResume() ?? result;
        if (result is null) { pendingOAuth = false; return; }
        ApplyResume(result);
    }
    private void OAuthResumed()
    {
        if (!disposed) _ = InvokeAsync(() => { if (Accounts.TakeOAuthResume() is { } value) ApplyResume(value); StateHasChanged(); });
    }
    private void ApplyResume(BrowserOAuthResume result)
    {
        if (result.FamilyId is { } expected && Api.Session.FamilyId != expected) return;
        pendingOAuth = !result.Completed;
        if (!result.Completed) { status = "Подтверждение пока не завершено. Завершите вход у провайдера и проверьте результат."; return; }
        if (result.ProofReady)
        {
            if (!Accounts.HasProof(result.Action)) { status = "Подтверждение устарело. Откройте действие и подтвердите вход ещё раз."; return; }
            dialogFamily = Api.Session.FamilyId;
            if (result.Action.StartsWith("link:", StringComparison.Ordinal) || result.Action.StartsWith("unlink:", StringComparison.Ordinal))
            { var pieces = result.Action.Split(':'); dialog = pieces[0]; selectedProvider = pieces[1]; }
            else dialog = result.Action switch { "set_password" => "set-password", "set_recovery_email" => "email", "delete_account" => "delete", _ => "export" };
            status = "Вход подтверждён. Завершите действие в открытой форме.";
        }
        else status = result.Action == "login" ? "Вход выполнен." : "Способ входа подключён.";
    }
    private void ProofReady() => StateHasChanged();
    private static void Match(string value, string repeated) { if (value != repeated) throw new InvalidOperationException("Пароли не совпадают."); }
    private static string ProviderLabel(string value) => value == "vk" ? "VK ID" : "Яндекс ID";
    private bool ProviderConfigured(string provider) => provider == "vk" ? Api.Session.Capabilities.Vk : Api.Session.Capabilities.Yandex;
    private static string PlatformLabel(string value) => value switch { "windows" => "Windows", "android" => "Android", "web" => "Браузер", _ => "Устройство" };
    private void EnsureOperation()
    {
        if (operationFamily is { } expected && (!Api.Session.Authenticated || Api.Session.FamilyId != expected))
            throw new BrowserApiException(409, "account_changed", "Аккаунт изменился. Повторите действие в текущем аккаунте.");
    }
    private async Task Run(Func<CancellationToken, Task> action)
    {
        if (busy || disposed) return;
        busy = true; error = null; status = null; operationFamily = dialogFamily;
        var token = operation.Token;
        try { EnsureOperation(); await action(token); }
        catch (OperationCanceledException) { }
        catch (BrowserApiException exception) { error = ErrorText(exception); }
        catch (InvalidOperationException exception) { error = exception.Message; }
        catch (ArgumentException) { error = "Проверьте поля: имя пользователя, длину пароля и адрес почты."; }
        catch (JSException) { error = "Браузер не разрешил действие. Проверьте доступ к хранилищу и повторите попытку."; }
        finally { ClearSecrets(); busy = false; operationFamily = null; if (!disposed) await InvokeAsync(StateHasChanged); }
    }
    public static string ErrorText(BrowserApiException exception) => exception.Code switch
    {
        "username_unavailable" => "Это имя пользователя уже занято. Выберите другое.",
        "session_changed" or "account_changed" => "Аккаунт изменился в другой вкладке. Откройте форму заново.",
        "invalid_external_proof" => "Подтверждение входа устарело или уже использовано. Подтвердите вход ещё раз.",
        "last_login_method" => "Нельзя отключить последний способ входа. Сначала подключите другой.",
        "identity_unavailable" => "Этот аккаунт провайдера уже связан с другим пользователем.",
        "password_already_set" => "Пароль уже задан. Используйте смену пароля.",
        "export_not_found" => "Срок хранения копии истёк или она недоступна. Подготовьте новую копию.",
        "external_attempt_expired" => "Время входа истекло. Начните новую попытку.",
        _ => exception.Message
    };
    private void ClearSecrets() { password = ""; passwordAgain = ""; currentPassword = ""; newPassword = ""; verificationCode = ""; resetCode = ""; }
    public void Dispose()
    {
        disposed = true; Api.SessionChanged -= SessionChanged; State.Changed -= Changed; Accounts.OAuthResumed -= OAuthResumed;
        operation.Cancel(); operation.Dispose(); ClearSecrets(); Accounts.ClearProof();
    }
}
