using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Osr2PlusPlugin.Views;

/// <summary>
/// Code-behind for the funscript visualizer bottom panel.
/// Renders multi-axis graph or speed-based heatmap using SkiaSharp.
/// </summary>
public partial class VisualizerView : UserControl
{
    private VisualizerViewModel? _viewModel;

    // ── Heatmap color stops (speed → color) ──────────────────
    private static readonly (float speed, SKColor color)[] HeatmapStops =
    [
        (0f,   SKColor.Parse("#1B0A7A")),
        (100f, SKColor.Parse("#2989D8")),
        (200f, SKColor.Parse("#46B946")),
        (300f, SKColor.Parse("#F0C000")),
        (400f, SKColor.Parse("#FF4500")),
        (500f, SKColor.Parse("#FF0000")),
    ];

    public VisualizerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        CompositionTarget.Rendering += OnRendering;
    }

    // ── DataContext wiring ────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as VisualizerViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateEmptyState();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisualizerViewModel.HasScripts))
            UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = _viewModel?.HasScripts == true
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    // ── Render loop (60 fps via CompositionTarget) ───────────

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_viewModel?.HasScripts == true && IsVisible)
            SkiaCanvas.InvalidateVisual();
    }

    // ── Paint surface entry point ────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColor.Parse("#1E1E1E"));

        if (_viewModel == null || !_viewModel.HasScripts)
            return;

        var width = info.Width;
        var height = info.Height;

        const float marginLeft = 5f;
        const float marginRight = 5f;
        const float marginTop = 24f;   // space for legend
        const float marginBottom = 5f;

        var chartLeft = marginLeft;
        var chartRight = width - marginRight;
        var chartTop = marginTop;
        var chartBottom = height - marginBottom;
        var chartWidth = chartRight - chartLeft;
        var chartHeight = chartBottom - chartTop;

        if (chartWidth <= 0 || chartHeight <= 0)
            return;

        var currentTime = _viewModel.CurrentTime;
        var windowRadius = _viewModel.TimeWindowRadius;
        var windowStart = currentTime - windowRadius;
        var windowEnd = currentTime + windowRadius;

        if (_viewModel.SelectedMode == VisualizationMode.Heatmap)
        {
            DrawHeatmap(canvas, chartLeft, chartTop, chartWidth, chartHeight, windowStart, windowEnd);
        }
        else
        {
            DrawGrid(canvas, chartLeft, chartTop, chartWidth, chartHeight, windowStart, windowEnd);

            foreach (var (axisId, data) in _viewModel.LoadedAxes)
            {
                var colorHex = VisualizerViewModel.AxisColors.GetValueOrDefault(axisId, "#FFFFFF");
                DrawAxis(canvas, data, chartLeft, chartTop, chartWidth, chartHeight,
                         windowStart, windowEnd, colorHex);
            }
        }

        DrawCursor(canvas, chartLeft, chartTop, chartWidth, chartHeight, currentTime, windowStart, windowEnd);
        DrawTimeLabels(canvas, chartLeft, chartTop, chartWidth, chartHeight, windowStart, windowEnd);
        DrawLegend(canvas, width);
    }

    // ══════════════════════════════════════════════════════════
    //  Graph Mode — Grid, Axes, Cursor, Legend
    // ══════════════════════════════════════════════════════════

    private static void DrawGrid(SKCanvas canvas, float left, float top,
        float width, float height, double windowStart, double windowEnd)
    {
        using var gridPaint = new SKPaint
        {
            Color = SKColor.Parse("#2A2A2A"),
            StrokeWidth = 1,
            IsAntialias = false,
            Style = SKPaintStyle.Stroke,
        };

        // Horizontal grid lines at position 0, 25, 50, 75, 100
        for (int pos = 0; pos <= 100; pos += 25)
        {
            float y = top + height - (pos / 100f * height);
            canvas.DrawLine(left, y, left + width, y, gridPaint);
        }

        // Vertical grid lines every 5 seconds
        var startSecond = Math.Ceiling(windowStart / 5.0) * 5.0;
        for (var t = startSecond; t <= windowEnd; t += 5.0)
        {
            var x = left + (float)((t - windowStart) / (windowEnd - windowStart) * width);
            if (x >= left && x <= left + width)
                canvas.DrawLine(x, top, x, top + height, gridPaint);
        }
    }

    private static void DrawAxis(SKCanvas canvas, FunscriptData data,
        float left, float top, float width, float height,
        double windowStart, double windowEnd, string colorHex)
    {
        if (data.Actions.Count == 0)
            return;

        using var paint = new SKPaint
        {
            Color = SKColor.Parse(colorHex),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        var windowDuration = windowEnd - windowStart;
        using var path = new SKPath();
        bool started = false;

        // Binary search for the first action in the visible window (with 1s padding)
        var searchStartMs = (long)((windowStart - 1) * 1000);
        int startIdx = BinarySearchStart(data.Actions, searchStartMs);
        if (startIdx > 0) startIdx--; // include one point before for line continuity

        for (int i = startIdx; i < data.Actions.Count; i++)
        {
            var action = data.Actions[i];
            var timeSec = action.AtMs / 1000.0;

            if (timeSec > windowEnd + 1)
                break;

            float x = left + (float)((timeSec - windowStart) / windowDuration * width);
            float y = top + height - (action.Pos / 100f * height);

            if (!started)
            {
                path.MoveTo(x, y);
                started = true;
            }
            else
            {
                path.LineTo(x, y);
            }
        }

        if (started)
            canvas.DrawPath(path, paint);
    }

    // ══════════════════════════════════════════════════════════
    //  Heatmap Mode
    // ══════════════════════════════════════════════════════════

    private void DrawHeatmap(SKCanvas canvas, float left, float top,
        float width, float height, double windowStart, double windowEnd)
    {
        // Heatmap renders L0 (Stroke) only
        if (_viewModel == null ||
            !_viewModel.LoadedAxes.TryGetValue("L0", out var data) ||
            data.Actions.Count < 2)
            return;

        using var barPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        var windowDuration = windowEnd - windowStart;
        int pixelWidth = (int)width;
        if (pixelWidth <= 0) return;

        for (int px = 0; px < pixelWidth; px++)
        {
            // Time range for this pixel column
            double t0 = windowStart + (px / (double)pixelWidth) * windowDuration;
            double t1 = windowStart + ((px + 1) / (double)pixelWidth) * windowDuration;
            double tMidMs = ((t0 + t1) / 2.0) * 1000.0;

            // Find surrounding actions via binary search
            int idx = BinarySearchStart(data.Actions, (long)tMidMs);
            if (idx >= data.Actions.Count) idx = data.Actions.Count - 1;
            if (idx <= 0) idx = 1;

            var a0 = data.Actions[idx - 1];
            var a1 = data.Actions[idx];

            // Speed = |position delta| / time delta (pos/sec)
            double dtMs = a1.AtMs - a0.AtMs;
            double speed = dtMs > 0
                ? Math.Abs(a1.Pos - a0.Pos) / (dtMs / 1000.0)
                : 0;

            barPaint.Color = SpeedToColor((float)speed);
            float x = left + px;
            canvas.DrawRect(x, top, 1, height, barPaint);
        }
    }

    /// <summary>
    /// Maps a speed value (pos/sec) to a color using the 6-stop gradient.
    /// </summary>
    internal static SKColor SpeedToColor(float speed)
    {
        if (speed <= HeatmapStops[0].speed)
            return HeatmapStops[0].color;

        for (int i = 1; i < HeatmapStops.Length; i++)
        {
            if (speed <= HeatmapStops[i].speed)
            {
                float t = (speed - HeatmapStops[i - 1].speed) /
                          (HeatmapStops[i].speed - HeatmapStops[i - 1].speed);
                return LerpColor(HeatmapStops[i - 1].color, HeatmapStops[i].color, t);
            }
        }

        return HeatmapStops[^1].color;
    }

    private static SKColor LerpColor(SKColor a, SKColor b, float t)
    {
        return new SKColor(
            (byte)(a.Red + (b.Red - a.Red) * t),
            (byte)(a.Green + (b.Green - a.Green) * t),
            (byte)(a.Blue + (b.Blue - a.Blue) * t),
            (byte)(a.Alpha + (b.Alpha - a.Alpha) * t));
    }

    // ══════════════════════════════════════════════════════════
    //  Shared — Cursor, Time Labels, Legend, Binary Search
    // ══════════════════════════════════════════════════════════

    private static void DrawCursor(SKCanvas canvas, float left, float top,
        float width, float height, double currentTime,
        double windowStart, double windowEnd)
    {
        var x = left + (float)((currentTime - windowStart) / (windowEnd - windowStart) * width);

        using var cursorPaint = new SKPaint
        {
            Color = SKColors.White.WithAlpha(200),
            StrokeWidth = 1.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        canvas.DrawLine(x, top, x, top + height, cursorPaint);
    }

    private static void DrawTimeLabels(SKCanvas canvas, float left, float top,
        float width, float height, double windowStart, double windowEnd)
    {
        using var timePaint = new SKPaint
        {
            Color = SKColor.Parse("#555555"),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = 10,
            Typeface = SKTypeface.FromFamilyName("Consolas"),
        };

        var startSecond = Math.Ceiling(windowStart / 5.0) * 5.0;
        for (var t = startSecond; t <= windowEnd; t += 5.0)
        {
            var x = left + (float)((t - windowStart) / (windowEnd - windowStart) * width);
            if (x < left || x > left + width)
                continue;

            if (t < 0) continue;

            var minutes = (int)(t / 60);
            var seconds = (int)(t % 60);
            var label = $"{minutes}:{seconds:D2}";
            canvas.DrawText(label, x + 2, top + height - 2, timePaint);
        }
    }

    private void DrawLegend(SKCanvas canvas, float canvasWidth)
    {
        if (_viewModel == null) return;

        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            TextSize = 11,
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
        };
        using var boxPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        float x = 8f;
        float y = 14f;

        // In heatmap mode, only show L0 legend
        var axes = _viewModel.SelectedMode == VisualizationMode.Heatmap
            ? _viewModel.LoadedAxes
                .Where(kv => kv.Key == "L0")
                .Select(kv => (kv.Key, kv.Value))
            : _viewModel.LoadedAxes
                .Select(kv => (kv.Key, kv.Value));

        foreach (var (axisId, _) in axes)
        {
            var colorHex = VisualizerViewModel.AxisColors.GetValueOrDefault(axisId, "#FFFFFF");
            var name = VisualizerViewModel.AxisNames.GetValueOrDefault(axisId, axisId);
            var color = SKColor.Parse(colorHex);

            // Color square
            boxPaint.Color = color;
            canvas.DrawRect(x, y - 8, 10, 10, boxPaint);

            // Axis name label
            textPaint.Color = SKColor.Parse("#AAAAAA");
            x += 14;
            canvas.DrawText(name, x, y, textPaint);
            x += textPaint.MeasureText(name) + 12;
        }
    }

    /// <summary>
    /// Binary search: returns the index of the first action at or after <paramref name="targetMs"/>.
    /// </summary>
    internal static int BinarySearchStart(List<FunscriptAction> actions, long targetMs)
    {
        int lo = 0, hi = actions.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (actions[mid].AtMs < targetMs)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return lo;
    }
}
