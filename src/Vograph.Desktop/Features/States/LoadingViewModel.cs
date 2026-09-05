using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop.Features.States;

public sealed class LoadingViewModel : ViewModelBase
{
    public LoadingViewModel(AppServices app) : base(app) { }
    public string Title => T("loadingTitle");
}
