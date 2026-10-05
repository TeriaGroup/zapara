using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Vograph.Desktop.Controls;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class PlannerTouchNavigationTests
{
    [AvaloniaFact]
    public void Losing_capture_cancels_navigation_even_if_release_returns_to_content()
    {
        using var scene = new TouchScene();
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
        scene.Down(pointer);
        scene.Move(pointer, new Point(80, 100));
        Assert.Same(scene.Content, pointer.Captured);

        pointer.Capture(null);
        scene.Up(pointer, new Point(80, 100));

        Assert.Empty(scene.Navigation);
    }

    [AvaloniaFact]
    public void A_fresh_primary_touch_recovers_after_scroll_finished_without_routed_release()
    {
        using var scene = new TouchScene();
        using (var scroll = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true))
        {
            scene.Down(scroll);
            scene.Move(scroll, new Point(200, 180));
            // A scroll recognizer or TouchCancel may finish without PointerReleased on the root.
        }

        using var next = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
        scene.Down(next);
        scene.Move(next, new Point(80, 100));
        scene.Up(next, new Point(80, 100));

        Assert.Equal(new[] { 1 }, scene.Navigation);
    }

    [AvaloniaFact]
    public void A_second_still_down_finger_does_not_start_a_fresh_gesture()
    {
        using var scene = new TouchScene();
        using var first = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true);
        using var second = new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, false);
        scene.Down(first);
        scene.Down(second);
        scene.Move(second, new Point(80, 100));
        scene.Up(second, new Point(80, 100));
        scene.Move(first, new Point(80, 100));
        scene.Up(first, new Point(80, 100));

        Assert.Empty(scene.Navigation);
    }

    private sealed class TouchScene : IDisposable
    {
        public Border Content { get; } = new();
        public List<int> Navigation { get; } = new();
        private readonly Window _window;

        public TouchScene()
        {
            _ = new PlannerTouchNavigation(Content, () => true, () => 1, Navigation.Add);
            _window = new Window { Content = Content, Width = 400, Height = 300 };
            _window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public void Down(IPointer pointer) => Content.RaiseEvent(new PointerPressedEventArgs(
            Content, pointer, _window, new Point(200, 100), 0, new PointerPointProperties(), KeyModifiers.None));

        public void Move(IPointer pointer, Point position) => Content.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent, Content, pointer, _window, position, 1,
            new PointerPointProperties(), KeyModifiers.None));

        public void Up(IPointer pointer, Point position) => Content.RaiseEvent(new PointerReleasedEventArgs(
            Content, pointer, _window, position, 2, new PointerPointProperties(), KeyModifiers.None, MouseButton.Left));

        public void Dispose() => _window.Close();
    }
}
