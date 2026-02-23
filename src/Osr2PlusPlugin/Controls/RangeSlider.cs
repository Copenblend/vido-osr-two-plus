using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Osr2PlusPlugin.Controls;

/// <summary>
/// A dual-thumb range slider for selecting a min/max range.
/// Two draggable thumbs (Min and Max) with a 1-unit minimum gap.
/// Dragging the highlighted region between thumbs moves both together,
/// preserving the range width. Supports configurable TrackColor for
/// axis-specific coloring.
/// </summary>
public class RangeSlider : Control
{
    // ═══════════════════════════════════════════════════════
    //  Dependency Properties
    // ═══════════════════════════════════════════════════════

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRangePropertyChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRangePropertyChanged));

    public static readonly DependencyProperty MinValueProperty =
        DependencyProperty.Register(nameof(MinValue), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
                OnMinValueChanged, CoerceMinValue));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(RangeSlider),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
                OnMaxValueChanged, CoerceMaxValue));

    public static readonly DependencyProperty TrackColorProperty =
        DependencyProperty.Register(nameof(TrackColor), typeof(Brush), typeof(RangeSlider),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The minimum value of the slider range (default 0).</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>The maximum value of the slider range (default 100).</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>The current minimum selected value (two-way binding).</summary>
    public double MinValue
    {
        get => (double)GetValue(MinValueProperty);
        set => SetValue(MinValueProperty, value);
    }

    /// <summary>The current maximum selected value (two-way binding).</summary>
    public double MaxValue
    {
        get => (double)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    /// <summary>Brush for the selected range fill between thumbs (axis color).</summary>
    public Brush TrackColor
    {
        get => (Brush)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    // ═══════════════════════════════════════════════════════
    //  Template Parts
    // ═══════════════════════════════════════════════════════

    private Canvas? _trackCanvas;
    private Rectangle? _trackBackground;
    private Rectangle? _trackFill;
    private Ellipse? _minThumb;
    private Ellipse? _maxThumb;

    // ═══════════════════════════════════════════════════════
    //  Drag State
    // ═══════════════════════════════════════════════════════

    private enum DragTarget { None, MinThumb, MaxThumb, Range }
    private DragTarget _dragTarget = DragTarget.None;
    private double _dragStartX;
    private double _dragStartMinValue;
    private double _dragStartMaxValue;

    // ═══════════════════════════════════════════════════════
    //  Constants
    // ═══════════════════════════════════════════════════════

    private const double ThumbDiameter = 14.0;
    private const double ThumbRadius = ThumbDiameter / 2.0;
    private const double TrackHeight = 3.0;
    private const double MinGap = 1.0;

    // ═══════════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════════

    static RangeSlider()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RangeSlider),
            new FrameworkPropertyMetadata(typeof(RangeSlider)));
    }

    public RangeSlider()
    {
        Cursor = Cursors.Hand;
        Focusable = true;
    }

    // ═══════════════════════════════════════════════════════
    //  Template Application
    // ═══════════════════════════════════════════════════════

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _trackCanvas = GetTemplateChild("PART_TrackCanvas") as Canvas;
        _trackBackground = GetTemplateChild("PART_TrackBackground") as Rectangle;
        _trackFill = GetTemplateChild("PART_TrackFill") as Rectangle;
        _minThumb = GetTemplateChild("PART_MinThumb") as Ellipse;
        _maxThumb = GetTemplateChild("PART_MaxThumb") as Ellipse;

        UpdateLayout();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateLayout();
    }

    // ═══════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════

    private new void UpdateLayout()
    {
        if (_trackCanvas == null || _trackBackground == null || _trackFill == null ||
            _minThumb == null || _maxThumb == null)
            return;

        var trackWidth = ActualWidth - ThumbDiameter; // usable track width (thumb centers)
        if (trackWidth <= 0) return;

        var range = Maximum - Minimum;
        if (range <= 0) return;

        var minFraction = (MinValue - Minimum) / range;
        var maxFraction = (MaxValue - Minimum) / range;

        var minCenterX = ThumbRadius + minFraction * trackWidth;
        var maxCenterX = ThumbRadius + maxFraction * trackWidth;
        var centerY = ActualHeight / 2.0;

        // Track background — full width, vertically centered
        _trackBackground.Width = ActualWidth;
        _trackBackground.Height = TrackHeight;
        Canvas.SetLeft(_trackBackground, 0);
        Canvas.SetTop(_trackBackground, centerY - TrackHeight / 2.0);

        // Track fill — between min and max thumb centers
        var fillLeft = minCenterX;
        var fillWidth = maxCenterX - minCenterX;
        _trackFill.Width = Math.Max(0, fillWidth);
        _trackFill.Height = TrackHeight;
        Canvas.SetLeft(_trackFill, fillLeft);
        Canvas.SetTop(_trackFill, centerY - TrackHeight / 2.0);

        // Min thumb
        Canvas.SetLeft(_minThumb, minCenterX - ThumbRadius);
        Canvas.SetTop(_minThumb, centerY - ThumbRadius);

        // Max thumb
        Canvas.SetLeft(_maxThumb, maxCenterX - ThumbRadius);
        Canvas.SetTop(_maxThumb, centerY - ThumbRadius);
    }

    // ═══════════════════════════════════════════════════════
    //  Mouse Handling
    // ═══════════════════════════════════════════════════════

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_trackCanvas == null) return;

        Focus();
        CaptureMouse();

        var pos = e.GetPosition(this);
        _dragStartX = pos.X;
        _dragStartMinValue = MinValue;
        _dragStartMaxValue = MaxValue;

        var trackWidth = ActualWidth - ThumbDiameter;
        if (trackWidth <= 0) return;

        var range = Maximum - Minimum;
        var minCenterX = ThumbRadius + (MinValue - Minimum) / range * trackWidth;
        var maxCenterX = ThumbRadius + (MaxValue - Minimum) / range * trackWidth;

        var distToMin = Math.Abs(pos.X - minCenterX);
        var distToMax = Math.Abs(pos.X - maxCenterX);

        // Check if clicking on a thumb (within thumb radius + 2px tolerance)
        var hitThreshold = ThumbRadius + 2;

        if (distToMin <= hitThreshold && distToMin <= distToMax)
        {
            _dragTarget = DragTarget.MinThumb;
        }
        else if (distToMax <= hitThreshold)
        {
            _dragTarget = DragTarget.MaxThumb;
        }
        else if (pos.X > minCenterX && pos.X < maxCenterX)
        {
            // Clicking between thumbs — drag range
            _dragTarget = DragTarget.Range;
        }
        else
        {
            // Click outside range — jump nearest thumb
            var clickValue = Minimum + (pos.X - ThumbRadius) / trackWidth * range;
            clickValue = Math.Round(Math.Clamp(clickValue, Minimum, Maximum));

            if (clickValue < MinValue)
            {
                _dragTarget = DragTarget.MinThumb;
                MinValue = clickValue;
            }
            else if (clickValue > MaxValue)
            {
                _dragTarget = DragTarget.MaxThumb;
                MaxValue = clickValue;
            }

            _dragStartX = pos.X;
            _dragStartMinValue = MinValue;
            _dragStartMaxValue = MaxValue;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragTarget == DragTarget.None) return;

        var pos = e.GetPosition(this);
        var trackWidth = ActualWidth - ThumbDiameter;
        if (trackWidth <= 0) return;

        var range = Maximum - Minimum;
        var deltaX = pos.X - _dragStartX;
        var deltaValue = deltaX / trackWidth * range;

        switch (_dragTarget)
        {
            case DragTarget.MinThumb:
            {
                var newMin = Math.Round(Math.Clamp(_dragStartMinValue + deltaValue, Minimum, MaxValue - MinGap));
                MinValue = newMin;
                break;
            }
            case DragTarget.MaxThumb:
            {
                var newMax = Math.Round(Math.Clamp(_dragStartMaxValue + deltaValue, MinValue + MinGap, Maximum));
                MaxValue = newMax;
                break;
            }
            case DragTarget.Range:
            {
                var rangeWidth = _dragStartMaxValue - _dragStartMinValue;
                var newMin = _dragStartMinValue + deltaValue;
                var newMax = _dragStartMaxValue + deltaValue;

                // Clamp to edges
                if (newMin < Minimum)
                {
                    newMin = Minimum;
                    newMax = Minimum + rangeWidth;
                }
                if (newMax > Maximum)
                {
                    newMax = Maximum;
                    newMin = Maximum - rangeWidth;
                }

                MinValue = Math.Round(newMin);
                MaxValue = Math.Round(newMax);
                break;
            }
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragTarget = DragTarget.None;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    // ═══════════════════════════════════════════════════════
    //  Keyboard Support
    // ═══════════════════════════════════════════════════════

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var step = shift ? 5.0 : 1.0;

        switch (e.Key)
        {
            case Key.Left:
                MinValue = Math.Max(Minimum, MinValue - step);
                e.Handled = true;
                break;
            case Key.Right:
                MinValue = Math.Min(MaxValue - MinGap, MinValue + step);
                e.Handled = true;
                break;
            case Key.Down:
                MaxValue = Math.Max(MinValue + MinGap, MaxValue - step);
                e.Handled = true;
                break;
            case Key.Up:
                MaxValue = Math.Min(Maximum, MaxValue + step);
                e.Handled = true;
                break;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Coercion & Change Callbacks
    // ═══════════════════════════════════════════════════════

    private static object CoerceMinValue(DependencyObject d, object value)
    {
        var slider = (RangeSlider)d;
        var v = (double)value;
        v = Math.Max(v, slider.Minimum);
        v = Math.Min(v, slider.MaxValue - MinGap);
        return Math.Round(v);
    }

    private static object CoerceMaxValue(DependencyObject d, object value)
    {
        var slider = (RangeSlider)d;
        var v = (double)value;
        v = Math.Min(v, slider.Maximum);
        v = Math.Max(v, slider.MinValue + MinGap);
        return Math.Round(v);
    }

    private static void OnMinValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;
        slider.CoerceValue(MaxValueProperty);
        slider.UpdateLayout();
    }

    private static void OnMaxValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;
        slider.CoerceValue(MinValueProperty);
        slider.UpdateLayout();
    }

    private static void OnRangePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (RangeSlider)d;
        slider.CoerceValue(MinValueProperty);
        slider.CoerceValue(MaxValueProperty);
        slider.UpdateLayout();
    }
}
