using System.Security.Cryptography;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services.Accounts;

namespace Vograph.Desktop.UiVerify;

/// <summary>Paired with Verify-AccountUi: only non-secret identities cross the fixture handshake.</summary>
public static class LogoutScenario
{
    public static void Run(Ui ui, Report report, Options options)
    {
        if (options.MinimumKeyboard) ui.SetMinimumWindow(options.Out);
        var name = "qa.logout." + Guid.NewGuid().ToString("N")[..12];
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        ui.SetAccountField("Account.Username", name);
        ui.Click("Account.Mode");
        Require(ui.WaitFor(() => ui.FindAll("Account.Register").Any(e => e.IsEnabled)), "registration unavailable");
        ui.SetAccountField("Account.Password", password);
        ui.Click("Account.Register");
        Require(ui.WaitText("Account.Status", s => s.Contains("создан")), "registration failed");
        ui.SetAccountField("Account.Password", password);
        ui.Click("Account.Login");
        ui.Click("Nav.Settings");
        Require(ui.WaitText("Account.Name", s => s.Equals(name, StringComparison.OrdinalIgnoreCase)), "login not published");
        var vault = new WindowsAccountSessionVault(Path.Combine(options.Data, "credentials"), new(options.AccountApi!));
        var identity = ReadIdentity(vault) ?? throw new InvalidOperationException("active vault missing");
        Act("Account.Refresh");
        Require(ui.WaitText("Account.Status", s => s.Contains("Профиль получен")), "fresh session me failed");
        Checkpoint("active");
        report.Pass("Fresh active session / exact SQL family", ui.Shot("logout-active-dark"));
        Act("Shell.ThemeToggle");
        report.Pass("Signed-in light", ui.Shot("logout-active-light"));
        if (options.MinimumKeyboard)
            LogoutKeyboard.CancelBothWays(ui, report, "light", VerifyCancelled);
        else
        {
            ui.Click("Account.Logout");
            Require(ui.WaitFor(() => ui.IsShown("Account.ConfirmLogout")), "confirmation absent");
            report.Pass("Confirmation light", ui.Shot("logout-confirm-light"));
            var cancel = ui.Window.FindAllDescendants(cf => cf.ByName("Отмена")).Single(e => e.Patterns.Invoke.IsSupported);
            ui.Invoke(cancel);
            Require(ui.WaitFor(() => !ui.IsShown("Account.ConfirmLogout")), "cancel did not dismiss confirmation");
            Require(ReadIdentity(vault) == identity && ui.IsShown("Account.Name"), "cancel changed local session");
        }
        Act("Account.Refresh");
        Require(ui.WaitText("Account.Status", s => s.Contains("Профиль получен")), "cancel invalidated session");
        Checkpoint("cancelled");
        report.Pass("Cancel preserves vault, account and live SQL family", ui.Shot("logout-cancelled-light"));
        Act("Shell.ThemeToggle");
        if (options.MinimumKeyboard)
            LogoutKeyboard.CancelBothWays(ui, report, "dark", VerifyCancelled);
        Act("Account.Logout");
        Require(ui.WaitFor(() => ui.IsShown("Account.ConfirmLogout")), "second confirmation absent");
        // Parent stops only its own Kestrel handle, and acknowledges process exit before this click.
        Checkpoint("ready");
        report.Pass("Confirmation dark / fixture connectivity established", ui.Shot("logout-confirm-dark"));
        Act("Account.ConfirmLogout");
        if (options.MinimumKeyboard) ui.Keys(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL, FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_8);
        else ui.Click("Nav.Settings");
        Require(ui.WaitFor(() => ui.FindAll("Account.Username").Any(e => e.IsEnabled)), "guest not restored");
        Require(ui.WaitFor(() => ReadIdentity(vault) is null), "vault not cleared");
        Require(!ui.IsShown("Account.Name") && ui.IsShown("Settings.Export"), "guest surface not restored");
        Require(ui.WaitText("Account.Status", s => s.Contains("не подтвержд")), "local-only warning missing");
        Require(ui.Text("Account.Password") == "", "guest password not cleared");
        if (options.Logout == "offline")
            Require(ui.WaitFor(() => LogLines().Any(s => s.Contains("remote logout not confirmed; local logout committed"))),
                "offline asynchronous failure completion not observed");
        Checkpoint("result");
        report.Pass(options.Logout == "online" ? "Exact family revoked in SQL; UI remains local-only" : "Offline local logout; SQL family remains live (NOT remote success)",
            ui.Shot("logout-guest-dark"));
        Act("Shell.ThemeToggle");
        report.Pass("Guest local-only warning light", ui.Shot("logout-guest-light"));
        Require(!LogLines().Any(s => s.Contains(name) || s.Contains(password) || s.Contains("accessToken") || s.Contains("refreshToken")), "secret log canary found");

        string[] LogLines() => Directory.GetFiles(Path.Combine(options.Data, "logs"), "desktop-*.log").SelectMany(File.ReadAllLines).ToArray();
        void Act(string id) { if (options.MinimumKeyboard) ui.Press(id); else ui.Click(id); }
        void VerifyCancelled()
        {
            Require(ReadIdentity(vault) == identity && ui.IsShown("Account.Name"), "keyboard cancellation changed local session");
        }
        void Checkpoint(string stage)
        {
            var path = Path.Combine(options.Out, "logout-" + stage);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { identity.UserId, identity.FamilyId }));
            File.Move(path + ".tmp", path + ".request.json");
            Require(ui.WaitFor(() => File.Exists(path + ".ok"), TimeSpan.FromSeconds(30)), "fixture checkpoint not acknowledged: " + stage);
        }
    }

    private static AccountSessionIdentity? ReadIdentity(WindowsAccountSessionVault vault)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var lease = vault.AcquireAsync(deadline.Token).GetAwaiter().GetResult();
        var entry = lease.Read();
        return entry is null ? null : AccountSessionIdentity.From(entry.Session);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
