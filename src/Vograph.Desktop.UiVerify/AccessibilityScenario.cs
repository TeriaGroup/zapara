using System.Security.Cryptography;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Microsoft.Win32;
using Microsoft.Data.Sqlite;

namespace Vograph.Desktop.UiVerify;

/// <summary>Bounded observations only. Never writes OS preferences or serializes UIA values.</summary>
public static class AccessibilityScenario
{
    public static void Run(Ui ui, Report report, Options options)
    {
        var facts = new List<object>();
        try
        {
            ui.SetMinimumWindow(options.Out);
            ui.Click("Nav.Settings");
            Require(ui.WaitFor(() => ui.Find("Account.Username").IsEnabled), "login unavailable");
            Theme(ui, report, facts);
            var guest = Homework(options.Data);
            Names(ui, facts, "Account.Username", "Account.Password", "Account.Login", "Account.Mode");
            Probe(ui, report, facts, "Account.Password");
            ui.SetAccountField("Account.Username", "qa.a." + Guid.NewGuid().ToString("N")[..12]);
            ui.SetAccountField("Account.Password", "");
            ui.Click("Account.Login");
            Require(ui.WaitText("Account.Status", s => s.Contains("3–32")), "validation status unavailable");
            Status(ui, facts, "validation");
            report.Pass("Validation discoverable via UIA (not announcement proof)", frame: ui.Shot("a11y-validation"));
            var credential = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            ui.Click("Account.Mode");
            Names(ui, facts, "Account.Register");
            ui.SetAccountField("Account.Password", credential);
            ui.Click("Account.Register");
            Require(ui.WaitText("Account.Status", s => s.Contains("создан")), "registration unavailable");
            ui.SetAccountField("Account.Password", credential);
            ui.Click("Account.Login");
            ui.Click("Nav.Settings");
            Require(ui.WaitFor(() => ui.TryFind("Account.Logout", TimeSpan.FromMilliseconds(100)) is not null), "login unavailable");
            Status(ui, facts, "signed-in");
            Names(ui, facts, "Account.DisplayName", "Account.Save", "Account.Refresh", "Account.Logout",
                "Account.Devices", "Account.CurrentPassword", "Account.NewPassword", "Account.ChangePassword");
            Probe(ui, report, facts, "Account.CurrentPassword");
            Probe(ui, report, facts, "Account.NewPassword");
            ui.Press("Account.Logout");
            Names(ui, facts, "Account.ConfirmLogout");
            ui.TabTo(ui.Find("Account.ConfirmLogout"));
            facts.Add(new { confirmationKeyboard = ui.HasFocus(ui.Find("Account.ConfirmLogout")) });
            var cancel = ui.Window.FindAllDescendants(cf => cf.ByName("Отмена")).Single(e => e.Patterns.Invoke.IsSupported);
            ui.TabTo(cancel);
            facts.Add(new { cancelName = "Отмена", cancelKeyboard = ui.HasFocus(cancel) });
            report.Pass("Confirmation names and keyboard reachability", frame: ui.Shot("a11y-confirmation"));
            ui.Keys(VirtualKeyShort.SPACE);
            Require(ui.WaitFor(() => !ui.IsShown("Account.ConfirmLogout")), "cancel failed");
            Require(ui.HasFocus(ui.Find("Account.Logout")), "cancel focus return failed");
            ui.Click("Account.Refresh");
            Require(ui.WaitFor(() => ui.Find("Account.Refresh").IsEnabled), "refresh unavailable");
            Require(Homework(options.Data) == guest, "guest changed");
            var profiles = Directory.GetDirectories(Path.Combine(options.Data, "profiles"));
            Require(profiles.Length == 1 && Homework(profiles[0]) == 0, "profile isolation failed");
            report.Pass("Guest/session isolation after cancel", "Guest rows unchanged; sole account database empty; signed-in controls retained");
        }
        catch (Exception ex)
        {
            // Deliberately omit exception messages: provider exceptions could quote an input value.
            report.Fail("Bounded accessibility branch stopped", ex.GetType().Name);
        }
        finally
        {
            facts.Add(new { screenReaderNarration = "unsupported: no screen reader operated", liveAnnouncements = "unproven: discoverability is not delivery",
                otherDpi = "unsupported: current 96 DPI only", osThemeChange = "not attempted", contrast = "not measured" });
            File.WriteAllText(Path.Combine(options.Out, "accessibility.json"), JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void Probe(Ui ui, Report report, List<object> facts, string id)
    {
        var canary = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var field = ui.Find(id);
        var representations = new List<object>();
        var leaked = false;
        void Observe(string representation, Func<string?> read)
        {
            try
            {
                var value = read();
                var exposed = value?.Contains(canary, StringComparison.Ordinal) == true;
                leaked |= exposed;
                representations.Add(new { representation, available = true, exposed });
            }
            catch { representations.Add(new { representation, available = false, unsupported = true }); }
        }
        try
        {
            ui.SetAccountField(id, canary);
            Observe("Name", () => field.Name);
            Observe("Value", () => field.Patterns.Value.Pattern.Value.Value);
            Observe("Text", () => field.Patterns.Text.Pattern.DocumentRange.GetText(-1));
            foreach (var property in field.GetSupportedPropertiesDirect())
                Observe("UIA:" + property.Id, () => field.FrameworkAutomationElement.GetPropertyValue(property)?.ToString());
            bool? password = null;
            try { password = field.Properties.IsPassword.Value; } catch { }
            facts.Add(new { field = id, isPasswordAvailable = password.HasValue, isPassword = password, leaked, representations });
            if (leaked) report.Fail(id + " exposure", "Synthetic canary exposed by one or more UIA representations; probe stopped and field cleared; see boolean-only evidence");
            else if (password != true) report.Fail(id + " password semantics", "IsPassword unsupported or false; not safety proof");
            else report.Pass(id + " observed representations", "No exposure in available representations ONLY; unsupported representations remain unproven");
        }
        finally { ui.SetAccountField(id, ""); }
    }

    private static void Names(Ui ui, List<object> facts, params string[] ids)
    {
        foreach (var id in ids)
        {
            var field = ui.Find(id);
            Require(field.Properties.ProcessId.Value == ui.Pid, "foreign element");
            var name = field.Name;
            Require(!string.IsNullOrWhiteSpace(name), "accessible name missing: " + id);
            facts.Add(new { id, name, enabled = field.IsEnabled, keyboardFocusable = field.Properties.IsKeyboardFocusable.ValueOrDefault });
        }
    }

    private static void Status(Ui ui, List<object> facts, string stage)
    {
        var status = ui.Find("Account.Status");
        string live;
        try { live = status.Properties.LiveSetting.Value.ToString(); } catch { live = "unsupported"; }
        facts.Add(new { stage, statusDiscoverable = !string.IsNullOrWhiteSpace(status.Name), liveSetting = live, announcementsVerified = false });
        Require(!string.IsNullOrWhiteSpace(status.Name), "empty status");
    }

    private static void Theme(Ui ui, Report report, List<object> facts)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false);
        var preference = key?.GetValue("AppsUseLightTheme");
        Require(preference is int value && value is 0 or 1, "current OS preference unsupported");
        ui.Click("SettingsTheme.0");
        Thread.Sleep(800);
        using var bitmap = ui.Capture();
        // Independently count rendered pixels, rather than trusting Choice or toggle label.
        var dark = 0; var light = 0;
        for (var y = 40; y < bitmap.Height; y += 4)
        for (var x = 0; x < bitmap.Width; x += 4)
        {
            var p = bitmap.GetPixel(x, y);
            if (p.R < 60 && p.G < 60 && p.B < 60) dark++;
            if (p.R > 220 && p.G > 220 && p.B > 220) light++;
        }
        var effectiveLight = light > dark;
        facts.Add(new { osAppsUseLightTheme = preference, renderedLight = effectiveLight, darkPixels = dark, lightPixels = light,
            selectionSupported = ui.Find("SettingsTheme.0").Patterns.SelectionItem.IsSupported,
            selected = ui.Find("SettingsTheme.0").Patterns.SelectionItem.Pattern.IsSelected.Value });
        var frame = ui.Shot("a11y-system-current");
        Require(effectiveLight == ((int)preference! == 1), "System rendered theme disagrees with current OS preference");
        report.Pass("System matches current OS preference", "Read-only AppsUseLightTheme independently compared with native rendered pixel counts; no OS transition claim", frame);
    }

    private static long Homework(string data)
    {
        using var db = new SqliteConnection("Data Source=" + Path.Combine(data, "vograph.db") + ";Mode=ReadOnly");
        db.Open(); using var command = db.CreateCommand(); command.CommandText = "SELECT count(*) FROM homework";
        return (long)command.ExecuteScalar()!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
