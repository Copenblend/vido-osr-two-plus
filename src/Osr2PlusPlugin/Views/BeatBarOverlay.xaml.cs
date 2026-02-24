using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Osr2PlusPlugin.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace Osr2PlusPlugin.Views;

/// <summary>
/// Beat bar overlay rendered with SkiaSharp. Displays scrolling beat dots,
/// an indicator ring at a fixed X position, and a glow effect when a beat
/// is within the threshold of the current playback position.
/// </summary>
public partial class BeatBarOverlay : UserControl
{
    // ── Visual constants ─────────────────────────────────────
    private const float BarHeight = 80f;
    private const float BeatRadius = 10f;
    private const float IndicatorOuterRadius = 12f;
    private const float IndicatorStrokeWidth = 2.5f;
    private const float GlowStrokeWidth = IndicatorStrokeWidth * 3f;  // 3× indicator stroke
    private const double TimeWindowMs = 3000.0;
    private const float IndicatorXRatio = 0.15f;
    private const double GlowThresholdMs = 80.0;
    private const byte BackgroundAlpha = 0x60;

    // ── Pre-allocated paints (reused every frame) ────────────
    private readonly SKPaint _backgroundPaint;
    private readonly SKPaint _beatDotPaint;
    private readonly SKPaint _indicatorPaint;
    private readonly SKPaint _glowPaint;
    private readonly SKPaint _trackLinePaint;

    // ── State ────────────────────────────────────────────────
    private SKElement? _skiaCanvas;
    private BeatBarViewModel? _viewModel;
    private bool _needsRepaint;

    // Fullscreen detection — cache the host's ControlsOverlay so we
    // can push the beat bar above it when it shares the same Grid row.
    private System.Windows.Controls.Border? _controlsOverlay;
    private double _lastBottomMargin;

    public BeatBarOverlay()
    {
        InitializeComponent();

        // Pre-allocate paints
        _backgroundPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, BackgroundAlpha),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        _beatDotPaint = new SKPaint
        {
            Color = SKColor.Parse("#F0F0F0"),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        _indicatorPaint = new SKPaint
        {
            Color = SKColor.Parse("#007ACC"),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = IndicatorStrokeWidth,
            IsAntialias = true,
        };

        _glowPaint = new SKPaint
        {
            Color = SKColor.Parse("#33AAEE"),  // lighter blue for flash effect
            Style = SKPaintStyle.Stroke,
            StrokeWidth = GlowStrokeWidth,
            IsAntialias = true,
        };

        _trackLinePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 40),  // subtle white line
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        // Create SKElement in code-behind (same pattern as VisualizerView)
        try
        {
            _skiaCanvas = new SKElement();
            _skiaCanvas.PaintSurface += OnPaintSurface;
            CanvasHost.Content = _skiaCanvas;
        }
        catch (Exception)
        {
            // SkiaSharp failed to load — overlay will be invisible
        }

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Lifecycle ────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += OnRendering;
        _controlsOverlay ??= FindControlsOverlay();
    }

    /// <summary>
    /// Walks up the visual tree to find the host's ControlsOverlay Border.
    /// Structure: RootGrid → PluginOverlayContainer (our parent) + ControlsOverlay (sibling).
    /// </summary>
    private System.Windows.Controls.Border? FindControlsOverlay()
    {
        DependencyObject? current = this;
        while (current != null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is System.Windows.Controls.Grid grid)
            {
                foreach (UIElement child in grid.Children)
                {
                    if (child is System.Windows.Controls.Border border &&
                        border.Name == "ControlsOverlay")
                        return border;
                }
            }
        }
        return null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
    }

    // ── DataContext wiring ───────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.RepaintRequested -= OnRepaintRequested;

        _viewModel = e.NewValue as BeatBarViewModel;

        if (_viewModel != null)
            _viewModel.RepaintRequested += OnRepaintRequested;
    }

    private void OnRepaintRequested()
    {
        _needsRepaint = true;
    }

    // ── Render loop (60 fps via CompositionTarget) ───────────

    private void OnRendering(object? sender, EventArgs e)
    {
        AdjustFullscreenMargin();

        if (_needsRepaint && IsVisible)
        {
            _needsRepaint = false;
            _skiaCanvas?.InvalidateVisual();
        }
    }

    /// <summary>
    /// In fullscreen the host moves ControlsOverlay into Row 0 (same row as the overlay
    /// container), so it covers the bottom of the video. Push our beat bar up above it.
    /// In normal mode the control bar is in Row 1, so no offset is needed.
    /// The margin stays constant even when the control bar fades in/out.
    /// </summary>
    private void AdjustFullscreenMargin()
    {
        if (_controlsOverlay == null) return;

        double target = System.Windows.Controls.Grid.GetRow(_controlsOverlay) == 0
            ? _controlsOverlay.ActualHeight
            : 0;

        if (Math.Abs(target - _lastBottomMargin) > 0.5)
        {
            _lastBottomMargin = target;
            Margin = new Thickness(0, 0, 0, target);
        }
    }

    // ── Paint surface entry point ────────────────────────────

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear(SKColors.Transparent);

        if (_viewModel == null || !_viewModel.IsActive)
            return;

        var width = (float)info.Width;
        var height = (float)info.Height;
        var beats = _viewModel.Beats;
        var currentTimeMs = _viewModel.CurrentTimeMs;

        if (beats.Count == 0 || width <= 0 || height <= 0)
            return;

        // ── Layout calculations ─────────────────────────────
        float indicatorX = width * IndicatorXRatio;
        float centerY = height / 2f;

        // ── Background ──────────────────────────────────────
        const float cornerRadius = 4f;
        canvas.DrawRoundRect(0, 0, width, height, cornerRadius, cornerRadius, _backgroundPaint);

        // Time range: the indicator position corresponds to currentTimeMs.
        // Left edge = currentTimeMs - (IndicatorXRatio * TimeWindowMs)
        // Right edge = currentTimeMs + ((1 - IndicatorXRatio) * TimeWindowMs)
        double timeAtLeft = currentTimeMs - IndicatorXRatio * TimeWindowMs;
        double timeAtRight = currentTimeMs + (1.0 - IndicatorXRatio) * TimeWindowMs;

        // ── Compute hit intensity (used to scale beats/indicator/glow) ──
        double closestDist = double.MaxValue;
        int nearIdx = BinarySearchStart(beats, currentTimeMs - GlowThresholdMs);
        for (int i = Math.Max(0, nearIdx - 1); i < beats.Count; i++)
        {
            double beatMs = beats[i];
            if (beatMs > currentTimeMs + GlowThresholdMs)
                break;

            double dist = Math.Abs(beatMs - currentTimeMs);
            if (dist < closestDist)
                closestDist = dist;
        }

        // intensity: 1.0 at exact hit → 0.0 at threshold edge
        float hitIntensity = closestDist <= GlowThresholdMs
            ? 1f - (float)(closestDist / GlowThresholdMs)
            : 0f;

        // Scale factor: 1.0 (normal) → 3.0 (at hit)
        float hitScale = 1f + hitIntensity * 2f;

        float scaledBeatRadius = BeatRadius * hitScale;
        float scaledIndicatorRadius = IndicatorOuterRadius * hitScale;
        // Glow stroke & blur stay at base size — the glow follows
        // the scaled indicator position, giving a natural scale effect
        // without extending beyond the bar bounds.
        float scaledGlowStroke = GlowStrokeWidth;

        // ── Draw horizontal track line ──────────────────────
        canvas.DrawLine(0, centerY, width, centerY, _trackLinePaint);

        // ── Draw beat dots ──────────────────────────────────
        int startIdx = BinarySearchStart(beats, timeAtLeft);
        for (int i = startIdx; i < beats.Count; i++)
        {
            double beatMs = beats[i];
            if (beatMs > timeAtRight)
                break;

            float x = (float)((beatMs - timeAtLeft) / (timeAtRight - timeAtLeft) * width);

            // Scale up the beat that is near the indicator
            float radius = BeatRadius;
            if (hitIntensity > 0 && Math.Abs(beatMs - currentTimeMs) <= GlowThresholdMs)
                radius = scaledBeatRadius;

            canvas.DrawCircle(x, centerY, radius, _beatDotPaint);
        }

        // ── Draw glow (if beat is within threshold) ─────────
        if (hitIntensity > 0)
        {
            byte glowAlpha = (byte)(220 * hitIntensity);

            _glowPaint.Color = new SKColor(51, 170, 238, glowAlpha);
            _glowPaint.StrokeWidth = scaledGlowStroke;
            _glowPaint.MaskFilter = SKMaskFilter.CreateBlur(
                SKBlurStyle.Normal, 8f);

            // Draw glow ring on the outer edge of the scaled indicator
            canvas.DrawCircle(indicatorX, centerY,
                scaledIndicatorRadius + scaledGlowStroke / 2f, _glowPaint);

            _glowPaint.MaskFilter?.Dispose();
            _glowPaint.MaskFilter = null;
            _glowPaint.StrokeWidth = GlowStrokeWidth;
        }

        // ── Draw indicator ring ─────────────────────────────
        canvas.DrawCircle(indicatorX, centerY, scaledIndicatorRadius, _indicatorPaint);
    }

    // ── Binary search ────────────────────────────────────────

    /// <summary>
    /// Returns the index of the first beat at or after <paramref name="targetMs"/>.
    /// </summary>
    private static int BinarySearchStart(List<double> beats, double targetMs)
    {
        int lo = 0, hi = beats.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (beats[mid] < targetMs)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return lo;
    }
}
