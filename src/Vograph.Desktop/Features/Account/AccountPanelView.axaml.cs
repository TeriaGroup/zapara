using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Vograph.Desktop.Features.Account;

public partial class AccountPanelView : UserControl
{
    public AccountPanelView() => InitializeComponent();

    private void OnCancelLogoutClick(object? sender, RoutedEventArgs e)
    {
        if (!this.IsAttachedToVisualTree() || sender is not Button { IsFocused: true } cancel ||
            !ReferenceEquals(e.Source, cancel) || !ReferenceEquals(cancel.DataContext, DataContext) ||
            DataContext is not AccountPanelViewModel { IsAccount: true, ConfirmLogout: true } ||
            !LogoutButton.IsEffectivelyVisible || !LogoutButton.IsEffectivelyEnabled) return;

        // Click precedes the bound command: move focus before Cancel hides its focused button.
        // No queued work can outlive this view; Tab keeps the keyboard focus indicator visible.
        LogoutButton.Focus(NavigationMethod.Tab);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as AccountPanelViewModel)?.ClearSecrets();
        base.OnDetachedFromVisualTree(e);
    }
}
