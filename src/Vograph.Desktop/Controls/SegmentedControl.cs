using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media.Transformation;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Controls;

/// <summary>Pill switcher with a sliding thumb (Вчера · Сегодня · Завтра). SelectedIndex = -1 hides the thumb.
/// The thumb's 200 ms slide-and-stretch (spec §7) is applied here, in code, from the nearest MotionSettings: as a
/// «/template/» style in Theme/Motion.axaml it was never withdrawn when the sheet was removed, so the thumb kept
/// gliding with «Анимации» off (T10 R9).</summary>
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
    private MotionSettings? _motion;

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
        ApplyMotion();
        Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Listen(MotionSettings.Resolve(this));
        ApplyMotion();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Listen(null);
        ApplyMotion();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty) Rebuild();
        else if (change.Property == SelectedIndexProperty) UpdateSelection();
    }

    /// <summary>Follows one MotionSettings while attached and none once detached: a handler left on the app-wide
    /// instance would keep this control — and the window it came with — alive. MotionSettings.Off never flips, so
    /// there is nothing to subscribe to there; the thumb simply never gets transitions.</summary>
    private void Listen(MotionSettings? motion)
    {
        if (ReferenceEquals(motion, MotionSettings.Off)) motion = null;
        if (ReferenceEquals(_motion, motion)) return;
        if (_motion is not null) _motion.PropertyChanged -= OnMotionChanged;
        _motion = motion;
        if (_motion is not null) _motion.PropertyChanged += OnMotionChanged;
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(MotionSettings.Enabled)) ApplyMotion();
    }

    /// <summary>The thumb glides only while «Анимации» is on AND it has been placed once: the transitions are this
    /// control's own local value, so _placed no longer has to hide a style value it could not withdraw.</summary>
    private void ApplyMotion()
    {
        if (_thumb is null) return;
        if (!_placed || _motion is not { Enabled: true } motion)
        {
            _thumb.ClearValue(Animatable.TransitionsProperty);
            return;
        }
        var duration = motion.Duration(200);
        _thumb.Transitions = new Transitions
        {
            new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = duration, Easing = MotionSettings.Ease },
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = duration, Easing = MotionSettings.Ease },
        };
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

        // The very first placement must not animate: Width starts as NaN and the thumb would slide in from the
        // left edge on every view load. Until _placed there are no transitions to hide (ApplyMotion withholds
        // them), so the geometry below lands at once and the glide starts with the second placement.
        _thumb.IsVisible = true;
        _thumb.Width = b.Width;
        _thumb.Height = b.Height;
        _thumb.RenderTransform = TransformOperations.Parse($"translateX({b.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}px)");

        if (!_placed)
        {
            _placed = true;
            ApplyMotion();
        }
    }
}
