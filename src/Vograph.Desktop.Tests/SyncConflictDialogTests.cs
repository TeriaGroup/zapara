using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Tests;

public sealed class SyncConflictDialogTests : UiTest
{
    private static readonly Guid EntityId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid NewOpId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static HomeworkValue LocalHomework(string text = "локальный черновик")
        => new("лек ИСТОРИЯ", "лек история", text, 1, Now, null);

    private static SyncRecord ServerHomework(long revision = 4, bool tombstone = false, string text = "серверная версия",
        DateTimeOffset? changedAt = null)
        => new("homework", EntityId, revision, tombstone,
            changedAt ?? Now,
            tombstone ? null : new HomeworkValue("лек ИСТОРИЯ", "лек история", text, 1, Earlier, null));

    private static PrivateSyncConflict ConflictEvent(string diagnostic = "Состояние записи не совпало.")
        => new("homework", EntityId, diagnostic);

    private static void EnsureLoc()
    {
        try { _ = Loc.Current; }
        catch (InvalidOperationException) { Loc.Init(new Vograph.Core.Services.I18nService("ru")); }
    }

    private static SyncConflictDialogViewModel ConflictDialog(
        SyncValue? local = null, SyncRecord? server = null, Guid? opId = null)
    {
        EnsureLoc();
        return SyncConflictDialogViewModel.ForConflict(
            ConflictEvent(), local ?? LocalHomework(), server ?? ServerHomework(), opId ?? NewOpId);
    }

    private static SyncConflictDialogViewModel ExpiredDialog()
    {
        EnsureLoc();
        return SyncConflictDialogViewModel.ForExpired(ConflictEvent("Снимок синхронизации устарел."));
    }

    [Fact]
    public void KeepLocal_uses_caller_opId_and_server_revision_never_auto_lww()
    {
        var olderServer = ServerHomework(revision: 7, text: "старее по времени", changedAt: Earlier);
        var newerLocal = LocalHomework("новее локально");
        var vm = ConflictDialog(newerLocal, olderServer, NewOpId);

        Assert.Null(vm.Decision);
        Assert.False(vm.IsExpired);
        Assert.True(vm.CanChooseVersion);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
        Assert.True(newerLocal.CreatedAtUtc > olderServer.ChangedAt);

        vm.KeepLocalCommand.Execute(null);

        var decision = vm.Decision!;
        Assert.Equal(SyncConflictKind.KeepLocal, decision.Kind);
        Assert.Equal(NewOpId, decision.NewOpId);
        Assert.Equal(7, decision.ExpectedRevision);
        Assert.Equal("upsert", decision.Action);
        Assert.False(decision.DropDraft);
        Assert.False(decision.AbortQueuedMutation);
        Assert.True(vm.Completion.IsCompleted);
    }

    [Fact]
    public void KeepServer_drops_local_draft_without_new_op()
    {
        var local = LocalHomework();
        var server = ServerHomework(revision: 9, tombstone: true);
        var vm = ConflictDialog(local, server);

        vm.KeepServerCommand.Execute(null);

        var decision = vm.Decision!;
        Assert.Equal(SyncConflictKind.KeepServer, decision.Kind);
        Assert.Null(decision.NewOpId);
        Assert.Null(decision.ExpectedRevision);
        Assert.True(decision.DropDraft);
        Assert.False(decision.AbortQueuedMutation);
        Assert.True(vm.Completion.IsCompleted);
    }

    [Fact]
    public void Enter_and_confirm_do_not_pick_a_side_for_revision_conflict()
    {
        var vm = ConflictDialog();
        var host = new DialogHostViewModel();
        var task = host.ShowAsync(vm);

        host.ConfirmCurrentCommand.Execute(null);
        vm.ConfirmCommand.Execute(null);

        Assert.False(task.IsCompleted);
        Assert.Null(vm.Decision);
        Assert.True(host.HasDialog);
    }

    [Fact]
    public async Task Cancel_leaves_revision_conflict_unresolved()
    {
        var vm = ConflictDialog();
        vm.CancelCommand.Execute(null);

        Assert.Null(vm.Decision);
        Assert.False(await vm.Completion);
    }

    [Fact]
    public async Task Expired_close_aborts_queued_mutation_and_does_not_replay()
    {
        var vm = ExpiredDialog();
        Assert.True(vm.IsExpired);
        Assert.False(vm.CanChooseVersion);
        Assert.False(vm.KeepLocalCommand.CanExecute(null));
        Assert.False(vm.KeepServerCommand.CanExecute(null));
        Assert.True(vm.ConfirmCommand.CanExecute(null));
        Assert.Null(vm.Decision);

        vm.ConfirmCommand.Execute(null);

        var decision = vm.Decision!;
        Assert.Equal(SyncConflictKind.Expired410, decision.Kind);
        Assert.True(decision.AbortQueuedMutation);
        Assert.True(decision.DropDraft);
        Assert.Null(decision.NewOpId);
        Assert.True(await vm.Completion);
    }

    [Fact]
    public async Task Expired_cancel_also_aborts()
    {
        var vm = ExpiredDialog();
        vm.CancelCommand.Execute(null);

        Assert.Equal(SyncConflictKind.Expired410, vm.Decision!.Kind);
        Assert.True(vm.Decision.AbortQueuedMutation);
        Assert.False(await vm.Completion);
    }

    [Fact]
    public void Copy_comes_from_russian_loc_keys_not_coordinator_diagnostic()
    {
        var conflict = ConflictDialog();
        var loc = Loc.Current;
        Assert.Equal(loc.T("syncConflictTitle"), conflict.Title);
        Assert.Equal(loc.T("syncConflictBody"), conflict.Body);
        Assert.Equal(loc.T("syncKeepLocal"), conflict.KeepLocalText);
        Assert.Equal(loc.T("syncKeepServer"), conflict.KeepServerText);
        Assert.NotEqual(ConflictEvent().Diagnostic, conflict.Body);
        Assert.Contains(conflict.Title, c => c is >= '\u0400' and <= '\u04FF');
        Assert.Contains(conflict.Body, c => c is >= '\u0400' and <= '\u04FF');

        var expired = ExpiredDialog();
        Assert.Equal(loc.T("syncConflictTitle"), expired.Title);
        Assert.Equal(loc.T("syncExpired"), expired.Body);
        Assert.Contains(expired.Body, c => c is >= '\u0400' and <= '\u04FF');
    }

    [Fact]
    public void Takes_PrivateSyncConflict_as_input()
    {
        EnsureLoc();
        var seen = ConflictEvent();
        var vm = SyncConflictDialogViewModel.ForConflict(seen, LocalHomework(), ServerHomework(), NewOpId);
        Assert.Same(seen, vm.Conflict);
        Assert.Equal("homework", vm.Conflict.EntityType);
        Assert.Equal(EntityId, vm.Conflict.EntityId);

        var expired = SyncConflictDialogViewModel.ForExpired(seen);
        Assert.Same(seen, expired.Conflict);
        Assert.True(expired.IsExpired);
    }

    [Fact]
    public void No_automatic_last_write_wins_on_dialog()
    {
        var type = typeof(SyncConflictDialogViewModel);
        Assert.Null(type.GetMethod("LastWriteWins"));
        Assert.Null(type.GetMethod("AutoResolve"));
        Assert.Null(type.GetMethod("ResolveByTimestamp"));
        var names = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(nameof(SyncConflictDialogViewModel.ForConflict), names);
        Assert.Contains(nameof(SyncConflictDialogViewModel.ForExpired), names);
        Assert.DoesNotContain("LastWriteWins", names);
        Assert.DoesNotContain("AutoResolve", names);
    }

    [Fact]
    public async Task KeepLocal_completes_host_true()
    {
        var host = new DialogHostViewModel();
        var vm = ConflictDialog();
        var task = host.ShowAsync(vm);
        vm.KeepLocalCommand.Execute(null);
        Assert.True(await task);
        Assert.Equal(SyncConflictKind.KeepLocal, vm.Decision!.Kind);
        Assert.False(host.HasDialog);
    }

    [AvaloniaFact]
    public void ViewLocator_maps_view_model_to_view()
    {
        var locator = new ViewLocator();
        var vm = ConflictDialog();
        Assert.True(locator.Match(vm));
        Assert.IsType<SyncConflictDialogView>(locator.Build(vm));
    }

    [AvaloniaFact]
    public async Task Renders_russian_keep_buttons_and_enter_does_not_resolve()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        db.Services.Theme = ThemeService.ForApplication(Avalonia.Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.Focus();
        SetTheme(ThemeVariant.Dark);

        var vm = ConflictDialog();
        var task = shell.Dialogs.ShowAsync(vm);
        Pump();

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(Loc.Current.T("syncConflictTitle"), texts);
        Assert.Contains(Loc.Current.T("syncConflictBody"), texts);

        var keepLocal = window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == "Dialog.KeepLocal");
        var keepServer = window.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == "Dialog.KeepServer");
        Assert.Equal(Loc.Current.T("syncKeepLocal"), keepLocal.Content as string);
        Assert.Equal(Loc.Current.T("syncKeepServer"), keepServer.Content as string);
        Assert.True(keepLocal.IsVisible);
        Assert.True(keepServer.IsVisible);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump();
        Assert.True(shell.Dialogs.HasDialog);
        Assert.Null(vm.Decision);
        Assert.False(task.IsCompleted);

        Click(window, keepLocal);
        Assert.True(await task);
        Assert.Equal(SyncConflictKind.KeepLocal, vm.Decision!.Kind);
        Assert.False(shell.Dialogs.HasDialog);
        AssertNoBindingErrors();
    }

    [AvaloniaFact]
    public async Task Expired_hides_keep_buttons_and_escape_aborts()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        db.Services.Theme = ThemeService.ForApplication(Avalonia.Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.Focus();

        var vm = ExpiredDialog();
        var task = shell.Dialogs.ShowAsync(vm);
        Pump();

        Assert.Contains(Loc.Current.T("syncExpired"),
            window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => AutomationProperties.GetAutomationId(b) is "Dialog.KeepLocal" or "Dialog.KeepServer"
                 && b.IsVisible && b.IsEffectivelyVisible);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pump();

        Assert.False(await task);
        Assert.Equal(SyncConflictKind.Expired410, vm.Decision!.Kind);
        Assert.True(vm.Decision.AbortQueuedMutation);
        AssertNoBindingErrors();
    }
}
