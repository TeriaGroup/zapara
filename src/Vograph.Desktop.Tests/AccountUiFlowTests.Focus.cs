using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Account;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed partial class AccountUiFlowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inline_logout_cancel_returns_keyboard_focus_on_repeated_invocation(bool escapeFirst)
    {
        await using var f = new Fixture();
        await f.Login();
        var view = new AccountPanelView { DataContext = f.Vm };
        var window = new Window { Content = view, Width = 960, Height = 600 };
        try
        {
            window.Show(); Settle(window);
            var origin = AccountButton(view, "Account.Logout");
            var confirm = AccountButton(view, "Account.ConfirmLogout");
            var cancel = CancelButton(view, f.Vm);
            var session = f.Vault.Entry;
            for (var i = 0; i < 3; i++)
            {
                Assert.True(origin.Focus(NavigationMethod.Tab));
                Press(window, Key.Enter, PhysicalKey.Enter);
                Assert.True(f.Vm.ConfirmLogout);
                Press(window, Key.Tab, PhysicalKey.Tab);
                Assert.Same(confirm, window.FocusManager!.GetFocusedElement());
                Press(window, Key.Tab, PhysicalKey.Tab);
                Assert.Same(cancel, window.FocusManager.GetFocusedElement());
                if (escapeFirst)
                {
                    Press(window, Key.Escape, PhysicalKey.Escape);
                    Assert.True(f.Vm.ConfirmLogout);
                    Assert.Same(cancel, window.FocusManager.GetFocusedElement());
                }
                Press(window, Key.Space, PhysicalKey.Space);
                Assert.False(f.Vm.ConfirmLogout);
                Assert.True(f.Vm.IsAccount);
                Assert.Same(session, f.Vault.Entry);
                Assert.Same(origin, window.FocusManager.GetFocusedElement());
                Assert.True(origin.IsFocused);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Inline_logout_cancel_does_not_steal_unrelated_focus()
    {
        await using var f = new Fixture();
        await f.Login();
        var view = new AccountPanelView { DataContext = f.Vm };
        var other = new Button { Content = "Other control" };
        var window = new Window { Content = new StackPanel { Children = { other, view } } };
        try
        {
            window.Show(); Assert.True(other.Focus()); Settle(window);
            Assert.Same(other, window.FocusManager!.GetFocusedElement());
            f.Vm.RequestLogoutCommand.Execute(null); Settle(window);
            Assert.Same(other, window.FocusManager.GetFocusedElement());
            // A programmatic routed Click while focus is elsewhere must not redirect keyboard input.
            CancelButton(view, f.Vm).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Vm.CancelLogoutCommand.Execute(null); Settle(window);
            Assert.Same(other, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Inline_logout_detach_and_reattach_do_not_queue_focus_restoration()
    {
        await using var f = new Fixture();
        await f.Login();
        var view = new AccountPanelView { DataContext = f.Vm };
        var other = new Button { Content = "Current view" };
        var panel = new StackPanel { Children = { other, view } };
        var window = new Window { Content = panel };
        try
        {
            window.Show();
            f.Vm.RequestLogoutCommand.Execute(null); Settle(window);
            var cancel = CancelButton(view, f.Vm);
            Assert.True(cancel.Focus(NavigationMethod.Tab));
            panel.Children.Remove(view);
            Assert.True(other.Focus());
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            f.Vm.CancelLogoutCommand.Execute(null); Settle(window);
            Assert.Same(other, window.FocusManager!.GetFocusedElement());
            panel.Children.Add(view); Settle(window);
            Assert.Same(other, window.FocusManager.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Inline_logout_replaced_data_context_does_not_restore_old_focus()
    {
        await using var f = new Fixture();
        await f.Login();
        using var replacement = new AccountPanelViewModel();
        var view = new AccountPanelView { DataContext = f.Vm };
        var other = new Button { Content = "Current control" };
        var window = new Window { Content = new StackPanel { Children = { other, view } } };
        try
        {
            window.Show();
            f.Vm.RequestLogoutCommand.Execute(null); Settle(window);
            Assert.True(CancelButton(view, f.Vm).Focus(NavigationMethod.Tab));
            view.DataContext = replacement;
            Assert.True(other.Focus());
            f.Vm.CancelLogoutCommand.Execute(null); Settle(window);
            Assert.Same(other, window.FocusManager!.GetFocusedElement());
        }
        finally { window.Close(); }
    }

    private static Button AccountButton(AccountPanelView view, string id) =>
        view.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == id);
    private static Button CancelButton(AccountPanelView view, AccountPanelViewModel vm) =>
        view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.CancelLogoutCommand));
    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    private static void Press(Window window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Settle(window);
    }
}
