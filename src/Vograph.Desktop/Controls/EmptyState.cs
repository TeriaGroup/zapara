using Avalonia;
using Avalonia.Controls.Primitives;
using System.Windows.Input;
using Avalonia.Media;

namespace Vograph.Desktop.Controls;

public class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));
    public static readonly StyledProperty<string?> HintProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(Hint));
    public static readonly StyledProperty<Geometry?> IconProperty = AvaloniaProperty.Register<EmptyState, Geometry?>(nameof(Icon));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>#21: одна основная кнопка пустого состояния («Выбрать группу», «Добавить задание»…); без текста не показывается.</summary>
    public static readonly StyledProperty<string?> ActionTextProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(Command));
    public static readonly StyledProperty<object?> CommandParameterProperty = AvaloniaProperty.Register<EmptyState, object?>(nameof(CommandParameter));
    /// <summary>AutomationId основной кнопки — UI-тесты ищут кнопку по нему (например «Chat.SignIn»).</summary>
    public static readonly StyledProperty<string?> ActionAutomationIdProperty = AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionAutomationId));

    public string? ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? Command { get => GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public string? ActionAutomationId { get => GetValue(ActionAutomationIdProperty); set => SetValue(ActionAutomationIdProperty, value); }
}
