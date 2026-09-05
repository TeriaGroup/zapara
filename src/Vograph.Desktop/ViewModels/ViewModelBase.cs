using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    protected ViewModelBase(AppServices app) => App = app;

    public AppServices App { get; }

    [ObservableProperty] private bool _isBusy;

    protected string T(string key, params object[] args) => App.Loc.T(key, args);

    /// <summary>
    /// Runs synchronous Core work off the UI thread. Failures become a toast + log entry and
    /// a null result — callers never see exceptions and the UI thread never blocks on SQLite.
    /// </summary>
    protected async Task<T?> RunAsync<T>(Func<T> work, string context) where T : class
    {
        IsBusy = true;
        try
        {
            return await Task.Run(work);
        }
        catch (Exception ex)
        {
            Report(context, ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task<bool> RunAsync(Action work, string context)
    {
        IsBusy = true;
        try
        {
            await Task.Run(work);
            return true;
        }
        catch (Exception ex)
        {
            Report(context, ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Report(string context, Exception ex)
    {
        App.Log.Error(context, ex);
        App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
    }
}
