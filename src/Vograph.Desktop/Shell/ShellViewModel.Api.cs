namespace Vograph.Desktop.Shell;

public sealed partial class ShellViewModel
{
    private sealed record ApiAvailability(bool Available);
    private async Task ShowUnavailableApiSelectionAsync()
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        if (!App.Api.Configured) return;
        var state = await RunAsync(() => new ApiAvailability(App.Api.HasSelectedCache), "api cache availability");
        if (state is { Available: false } && operation.IsCurrent)
            Current = new Features.States.ErrorStateViewModel(App,
                App.Api.LastError ?? "Расписание этой группы ещё не загружено. Подключитесь к сети и повторите загрузку.",
                () => StartAsync(App.AllowNetwork));
    }

    public async Task EnsureApiNeedsAsync()
    {
        if (!App.Api.Configured) return;
        await RefreshApiScheduleAsync(quiet: true, neededOnly: true);
    }

    private async Task<bool> RefreshApiScheduleAsync(bool quiet, bool neededOnly = false)
    {
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return false;
        IsRefreshing = true;
        try
        {
            var changed = await App.Api.RefreshAsync(neededOnly);
            if (!operation.IsCurrent) return false;
            await RefreshGroupCardAsync();
            if (changed)
            {
                _staleToastShown = false;
                RaiseScheduleChanged();
                await UpdateHomeworkBadgeAsync();
                if (!quiet) App.Toasts.Ok(T("refreshOk"));
            }
            else if (App.Api.LastError is not null || App.Api.ConfigurationError is not null)
            {
                if (!quiet || !_staleToastShown) App.Toasts.Warn(App.Api.LastError ?? App.Api.ConfigurationError!);
                _staleToastShown = true;
            }
            return changed;
        }
        finally { IsRefreshing = false; }
    }
}
