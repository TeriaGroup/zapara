using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.States;

public sealed partial class ErrorStateViewModel : ViewModelBase
{
    private readonly Func<Task> _retry;

    public ErrorStateViewModel(AppServices app, string? detail, Func<Task> retry) : base(app)
    {
        Detail = detail;
        _retry = retry;
    }

    public string Title => T("bootstrapError");
    public string Hint => T("bootstrapHint");
    public string? Detail { get; }
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);

    [RelayCommand]
    private Task Retry() => _retry();
}
