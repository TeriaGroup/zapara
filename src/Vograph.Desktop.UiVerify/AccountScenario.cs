using System.Security.Cryptography;
using FlaUI.Core.WindowsAPI;
using Microsoft.Data.Sqlite;

namespace Vograph.Desktop.UiVerify;

public static class AccountScenario
{
    public static void Run(Ui ui, Report report, Options options)
    {
        try
        {
            ui.Click("Nav.Settings");
            Require(ui.WaitFor(() => ui.Find("Account.Username").IsEnabled), "restoration did not enable login");
            Require(ui.TryFind("SettingsLanguage", TimeSpan.FromMilliseconds(200)) is null, "language selector remains");
            var guest = Count(Path.Combine(options.Data, "vograph.db"));
            Require(guest > 0, "guest canary absent");
            if (options.Logout is not null)
            {
                LogoutScenario.Run(ui, report, options);
                Require(Count(Path.Combine(options.Data, "vograph.db")) == guest, "guest data changed");
                var databases = Directory.GetFiles(Path.Combine(options.Data, "profiles"), "vograph.db", SearchOption.AllDirectories);
                Require(databases.Length == 1 && Count(databases[0]) == 0, "account/guest row isolation failed");
                report.Pass("Guest isolation after logout", "Guest homework retained; sole account database has zero homework");
                return;
            }
            if (options.RegistrationDisabled)
            {
                RegistrationDisabledScenario.Run(ui, report);
                Require(Count(Path.Combine(options.Data, "vograph.db")) == guest, "guest data changed");
                report.Pass("Гостевые данные сохранены", "Число записей домашки не изменилось");
                return;
            }
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            var names = new[] { "qa.a." + Guid.NewGuid().ToString("N")[..12], "qa.b." + Guid.NewGuid().ToString("N")[..12] };
            ui.Keys(VirtualKeyShort.TAB);
            report.Pass("Гость, русский интерфейс, клавиатурный фокус", ui.Shot("account-guest-dark-focus"));
            ui.Click("Shell.ThemeToggle");
            report.Pass("Светлая тема", ui.Shot("account-guest-light"));
            ui.Click("Shell.ThemeToggle");
            for (var i = 0; i < names.Length; i++)
            {
                ui.SetAccountField("Account.Username", names[i]);
                ui.SetAccountField("Account.Password", "short");
                ui.Click("Account.Login");
                Require(ui.WaitText("Account.Status", s => s.Contains("3–32")), "validation missing");
                Require(ui.WaitFor(() => ui.Find("Account.Mode").IsEnabled), "registration mode still busy");
                ui.Click("Account.Mode");
                Require(ui.WaitFor(() => ui.FindAll("Account.Register").Any(e => e.IsEnabled)), "registration form did not open");
                ui.SetAccountField("Account.Password", password);
                ui.Click("Account.Register");
                Require(ui.WaitText("Account.Status", s => s.Contains("создан")), "registration not confirmed");
                Require(ui.TryFind("Account.Name", TimeSpan.FromMilliseconds(200)) is null, "registration auto-logged in");
                report.Pass($"Регистрация {i + 1} без автовхода", ui.Shot($"account-created-{i}"));
                ui.SetAccountField("Account.Password", password);
                ui.Click("Account.Login");
                ui.Click("Nav.Settings");
                Require(ui.WaitText("Account.Name", s => s.Equals(names[i], StringComparison.OrdinalIgnoreCase)), "account name not published");
                Require(ui.TryFind("Settings.Export", TimeSpan.FromMilliseconds(200)) is null, "private legacy export exposed");
                ui.Click("Account.Refresh");
                Require(ui.WaitText("Account.Status", s => s.Contains("Профиль получен")), "server me not confirmed");
                var displayName = "Проверка профиля " + (i + 1);
                ui.SetAccountField("Account.DisplayName", displayName);
                ui.Click("Account.Save");
                Require(ui.WaitText("Account.Status", s => s.Contains("сохран")), "profile update not confirmed");
                ui.SetAccountField("Account.DisplayName", "");
                ui.Click("Account.Refresh");
                Require(ui.WaitFor(() => ui.Text("Account.DisplayName") == displayName), "server me did not return saved name");
                var databases = Directory.GetFiles(Path.Combine(options.Data, "profiles"), "vograph.db", SearchOption.AllDirectories);
                Require(databases.Length == i + 1 && databases.All(db => Count(db) == 0), "account/guest row isolation failed");
                ui.Click("Account.Devices");
                Require(ui.WaitText("Account.Status", s => s.Contains("Список устройств")), "devices not loaded");
                report.Pass($"Аккаунт {i + 1}: имя, отдельная БД, устройства", ui.Shot($"account-signed-in-{i}"));
                if (i == 0)
                {
                    ui.Click("Account.Revoke");
                    ui.Click("Nav.Settings");
                    Require(ui.WaitFor(() => ui.Find("Account.Username").IsEnabled), "revocation did not restore guest");
                    report.Pass("Отзыв текущей сессии возвращает гостя", ui.Shot("account-revoked"));
                }
                else
                {
                    ui.SetAccountField("Account.CurrentPassword", password);
                    ui.SetAccountField("Account.NewPassword", password + "new");
                    ui.Click("Account.ChangePassword");
                    ui.Click("Nav.Settings");
                    Require(ui.WaitFor(() => ui.Find("Account.Username").IsEnabled), "password change did not restore guest");
                    ui.SetAccountField("Account.Username", names[i]);
                    ui.SetAccountField("Account.Password", password);
                    ui.Click("Account.Login");
                    Require(ui.WaitText("Account.Status", s => s.Contains("Неверный")), "old password still accepted");
                    password += "new";
                    report.Pass("Смена пароля: выход и отклонение старого пароля", ui.Shot("account-password-changed"));
                }
                ui.SetAccountField("Account.Username", names[i]);
                ui.SetAccountField("Account.Password", password);
                ui.Click("Account.Login");
                ui.Click("Nav.Settings");
                Require(ui.WaitText("Account.Name", s => s.Equals(names[i], StringComparison.OrdinalIgnoreCase)), "re-login failed");
                ui.Click("Account.Logout");
                ui.Click("Account.ConfirmLogout");
                ui.Click("Nav.Settings");
                Require(ui.WaitFor(() => ui.Find("Account.Username").IsEnabled), "guest not restored");
                Require(Count(Path.Combine(options.Data, "vograph.db")) == guest, "guest data changed");
            }
            report.Pass("Гость после двух аккаунтов: домашка сохранена", ui.Shot("account-returned-guest"));
            var logs = Directory.GetFiles(Path.Combine(options.Data, "logs"), "*.log")
                .SelectMany(File.ReadAllLines).ToArray();
            Require(!logs.Any(line => line.Contains(password) || names.Any(line.Contains) ||
                line.Contains("accessToken") || line.Contains("refreshToken") || line.Contains("§5, задачи")), "private log canary found");
            report.Pass("Приватные данные и секреты отсутствуют в логах", "Проверены синтетические логины, пароль, token-поля и текст домашки");
            Require(ui.CloseGracefully(), "window did not exit gracefully");
        }
        catch (Exception ex)
        {
            report.Fail("Аккаунт: реальное окно", ex.GetType().Name + ": " + ex.Message);
            report.Fail("Checkpoint", ui.Shot("account-failure-masked"));
        }
        finally { ui.CloseGracefully(); }
    }

    private static int Count(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        using var command = db.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM homework";
        return Convert.ToInt32(command.ExecuteScalar());
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
