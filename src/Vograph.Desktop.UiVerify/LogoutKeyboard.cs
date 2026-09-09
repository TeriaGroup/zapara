using FlaUI.Core.WindowsAPI;

namespace Vograph.Desktop.UiVerify;

public static class LogoutKeyboard
{
    public static void CancelBothWays(Ui ui, Report report, string theme, Action verifySession)
    {
        foreach (var escape in new[] { true, false })
        {
            var method = escape ? "escape-then-cancel" : "cancel-space";
            ui.TabTo(ui.Find("Account.Logout"));
            report.Pass(theme + " invoking control focused / " + method, ui.FocusEvidence(), ui.Shot(theme + "-invoke-" + method));
            ui.Keys(VirtualKeyShort.RETURN);
            if (!ui.WaitFor(() => ui.IsShown("Account.ConfirmLogout"))) throw new InvalidOperationException("Enter did not open logout");
            ui.TabTo(ui.Find("Account.ConfirmLogout"));
            report.Pass(theme + " confirm reachable", ui.FocusEvidence(), ui.Shot(theme + "-confirm-" + method));
            var cancel = ui.Window.FindAllDescendants(cf => cf.ByName("Отмена")).Single(e => e.Patterns.Invoke.IsSupported);
            ui.TabTo(cancel);
            report.Pass(theme + " cancel reachable", ui.FocusEvidence(), ui.Shot(theme + "-cancel-" + method));
            if (escape)
            {
                // AccountPanelView uses an inline StackPanel, NOT the shell modal host.
                // Escape has no dismissal binding here; it must not log out or move focus.
                ui.Keys(VirtualKeyShort.ESCAPE);
                if (!ui.IsShown("Account.ConfirmLogout") || !ui.HasFocus(cancel))
                    throw new InvalidOperationException("Inline confirmation Escape changed visibility/focus unexpectedly");
                verifySession();
                report.Pass(theme + " inline Escape is non-destructive (not modal dismissal)", ui.FocusEvidence(), ui.Shot(theme + "-escape-unchanged"));
            }
            ui.Keys(VirtualKeyShort.SPACE);
            if (!ui.WaitFor(() => !ui.IsShown("Account.ConfirmLogout"))) throw new InvalidOperationException(method + " did not close dialog");
            // Observe BEFORE any input/navigation that might repair missing focus.
            var returned = ui.WaitFor(() => ui.HasFocus(ui.Find("Account.Logout")), TimeSpan.FromSeconds(2));
            var detail = ui.FocusEvidence();
            var frame = ui.Shot(theme + "-returned-" + method);
            if (returned) report.Pass(theme + " focus return / " + method, detail, frame);
            else report.Fail(theme + " focus return / " + method, "Expected Account.Logout; actual " + detail, frame);
            verifySession();
        }
    }
}
