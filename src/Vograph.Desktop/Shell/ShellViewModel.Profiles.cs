using Avalonia.Threading;

namespace Vograph.Desktop.Shell;

public sealed partial class ShellViewModel
{
    private bool attached;
    private bool suspended;
    private bool resumeAuto, resumeNotifications, resumeLan;
    public bool IsSuspended => suspended;
    public bool IsStopped => _stopped;

    public void Attach()
    {
        if (attached || _stopped) return;
        attached = true;
        App.Loc.LanguageChanged += LanguageChanged;
        if (App.Theme is { } theme) theme.Changed += ThemeChanged;
        App.LanSync.Imported += LanImported;
        if (App.PrivateSync is { } sync) sync.Conflict += OnPrivateSyncConflict;
    }

    private void DetachSubscriptions()
    {
        if (!attached) return;
        attached = false;
        App.Loc.LanguageChanged -= LanguageChanged;
        if (App.Theme is { } theme) theme.Changed -= ThemeChanged;
        App.LanSync.Imported -= LanImported;
        if (App.PrivateSync is { } sync) sync.Conflict -= OnPrivateSyncConflict;
    }

    private void ThemeChanged() { if (CanPublish && App.Theme is { } theme) IsDark = theme.IsDark; }
    private void LanguageChanged()
    {
        if (!CanPublish) return;
        foreach (var section in AllSections) section.RefreshLabel();
        _ = RefreshGroupCardAsync();
        OnPropertyChanged(nameof(GroupCardTip));
        OnPropertyChanged(nameof(SidebarToggleTip));
        OnPropertyChanged(nameof(MaximizeTip));
    }
    private void LanImported() => App.Work.Post(a => Dispatcher.UIThread.Post(a), NotifyImportedAsync,
        ex => App.Log.Error("lan import publication", ex));

    public void SuspendProducers()
    {
        if (suspended || _stopped) return;
        suspended = true;
        resumeAuto = IsAutoCheckRunning;
        resumeNotifications = App.NotificationScheduler.IsRunning;
        resumeLan = App.LanSync.IsRunning;
        App.Work.Suspend();
        _autoCheck?.Stop();
        _autoCheck = null;
        App.NotificationScheduler.Stop();
        App.LanSync.Stop();
        Dialogs.DismissCommand.Execute(null);
    }

    public void ResumeProducers()
    {
        if (!suspended || _stopped) return;
        suspended = false;
        App.Work.Resume();
        if (resumeAuto) StartAutoCheck();
        if (resumeNotifications) App.NotificationScheduler.Start();
        if (resumeLan && App.Profile.IsGuest) App.LanSync.Start();
    }
}
