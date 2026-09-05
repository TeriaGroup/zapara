using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Vograph.Desktop.ViewModels;

namespace Vograph.Desktop;

/// <summary>Maps a view model to its view by name: Features.Schedule.ScheduleViewModel → Features.Schedule.ScheduleView.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        var name = data!.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is null
            ? new TextBlock { Text = "View not found: " + name }
            : (Control)Activator.CreateInstance(type)!;
    }

    // Task 9 widens this to 'data is ViewModelBase or DialogViewModelBase'.
    public bool Match(object? data) => data is ViewModelBase;
}
