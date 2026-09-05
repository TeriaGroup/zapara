using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media.Transformation;

namespace Vograph.Desktop.Controls;

/// <summary>Pill switcher with a sliding thumb (Вчера · Сегодня · Завтра). SelectedIndex = -1 hides the thumb.</summary>
public class SegmentedControl : TemplatedControl
{
    public static readonly StyledProperty<IList<string>?> ItemsProperty =
        AvaloniaProperty.Register<SegmentedControl, IList<string>?>(nameof(Items));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<SegmentedControl, int>(nameof(SelectedIndex), -1, defaultBindingMode: BindingMode.TwoWay);

    private StackPanel? _panel;
    private Border? _thumb;
    private readonly List<Button> _buttons = new();
    private double _lastX = double.NaN, _lastWidth = double.NaN;
    private bool _placed;

    public IList<string>? Items { get => GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_panel != null) _panel.LayoutUpdated -= OnPanelLayoutUpdated;
        _panel = e.NameScope.Get<StackPanel>("PART_Items");
        _thumb = e.NameScope.Get<Border>("PART_Thumb");
        _panel.LayoutUpdated += OnPanelLayoutUpdated;
        _placed = false;
        _lastX = _lastWidth = double.NaN;
        Rebuild();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty) Rebuild();
        else if (change.Property == SelectedIndexProperty) UpdateSelection();
    }

    private void OnPanelLayoutUpdated(object? sender, EventArgs e) => PositionThumb();

    private void Rebuild()
    {
        if (_panel is null) return;
        _panel.Children.Clear();
        _buttons.Clear();
        var items = Items ?? Array.Empty<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var button = new Button { Content = items[i] };
            button.Classes.Add("seg");
            button.Click += (_, _) => SelectedIndex = index;
            _panel.Children.Add(button);
            _buttons.Add(button);
        }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (i == SelectedIndex) _buttons[i].Classes.Add("on");
            else _buttons[i].Classes.Remove("on");
        }
        _lastX = _lastWidth = double.NaN; // force re-position even if geometry is unchanged
        PositionThumb();
    }

    private void PositionThumb()
    {
        if (_thumb is null) return;
        if (SelectedIndex < 0 || SelectedIndex >= _buttons.Count) { _thumb.IsVisible = false; return; }
        var b = _buttons[SelectedIndex].Bounds;
        if (b.Width <= 0) return; // not laid out yet; LayoutUpdated will call again
        if (Math.Abs(b.X - _lastX) < 0.5 && Math.Abs(b.Width - _lastWidth) < 0.5 && _thumb.IsVisible) return;
        _lastX = b.X;
        _lastWidth = b.Width;

        // The very first placement must not animate: Width starts as NaN (nothing would be drawn until
        // the transition ends) and the thumb would slide in from the left edge on every view load.
        var transitions = _thumb.Transitions;
        if (!_placed) _thumb.Transitions = null;

        _thumb.IsVisible = true;
        _thumb.Width = b.Width;
        _thumb.Height = b.Height;
        _thumb.RenderTransform = TransformOperations.Parse($"translateX({b.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)");

        if (!_placed)
        {
            _thumb.Transitions = transitions;
            _placed = true;
        }
    }
}
