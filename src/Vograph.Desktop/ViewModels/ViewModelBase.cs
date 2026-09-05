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
    /// Runs synchronous Core work off the UI thread, one call at a time across the whole app
    /// (App.CoreGate — Core's SqliteConnection is not thread-safe). Failures become a toast + log
    /// entry and a null result — callers never see exceptions and the UI thread never blocks on SQLite.
    /// <paramref name="work"/> is synchronous, so it cannot start a nested RunAsync: no deadlock.
    /// </summary>
    protected async Task<T?> RunAsync<T>(Func<T> work, string context) where T : class
    {
        IsBusy = true;
        await App.CoreGate.WaitAsync();
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
            App.CoreGate.Release();
            IsBusy = false;
        }
    }

    /// <summary>Async Core work (e.g. network fetch followed by SQLite writes) under the same gate, off the UI thread.</summary>
    protected async Task<T?> RunAsync<T>(Func<Task<T>> work, string context) where T : class
    {
        IsBusy = true;
        await App.CoreGate.WaitAsync();
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
            App.CoreGate.Release();
            IsBusy = false;
        }
    }

    protected async Task<bool> RunAsync(Action work, string context)
    {
        IsBusy = true;
        await App.CoreGate.WaitAsync();
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
            App.CoreGate.Release();
            IsBusy = false;
        }
    }

    private void Report(string context, Exception ex)
    {
        App.Log.Error(context, ex);
        App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
    }
}
