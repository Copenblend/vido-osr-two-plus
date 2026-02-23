using Vido.Core.Events;
using Vido.Core.Playback;
using Vido.Core.Plugin;
using Osr2PlusPlugin.Services;
using Osr2PlusPlugin.ViewModels;
using Osr2PlusPlugin.Views;

namespace Osr2PlusPlugin;

/// <summary>
/// OSR2+ plugin entry point. Implements the Vido plugin lifecycle,
/// wires services together, subscribes to playback events, and
/// registers all UI contributions.
/// </summary>
public class Osr2PlusPlugin : IVidoPlugin
{
    private IPluginContext? _context;

    // Services
    private FunscriptParser? _parser;
    private FunscriptMatcher? _matcher;
    private FunscriptLoadingService? _scriptLoader;
    private TCodeService? _tcode;
    private InterpolationService? _interpolation;
    // private ITransportService? _transport;

    // ViewModels
    private SidebarViewModel? _sidebarVm;
    // private AxisControlViewModel? _axisControlVm;
    // private VisualizerViewModel? _visualizerVm;

    // Event subscriptions
    private readonly List<IDisposable> _subscriptions = new();

    public void Activate(IPluginContext context)
    {
        _context = context;

        // ── Create Services ──────────────────────────────────
        _parser = new FunscriptParser();
        _matcher = new FunscriptMatcher();
        _scriptLoader = new FunscriptLoadingService(_parser, _matcher);
        _interpolation = new InterpolationService();
        _tcode = new TCodeService(_interpolation);

        // ── Create ViewModels ────────────────────────────────
        _sidebarVm = new SidebarViewModel(_tcode, context.Settings);
        // _axisControlVm = new AxisControlViewModel(_tcode, _parser, _matcher, context.Settings);
        // _visualizerVm = new VisualizerViewModel(context.Settings);

        // ── Subscribe to Events ──────────────────────────────
        _subscriptions.Add(context.Events.Subscribe<VideoLoadedEvent>(OnVideoLoaded));
        _subscriptions.Add(context.Events.Subscribe<VideoUnloadedEvent>(OnVideoUnloaded));
        _subscriptions.Add(context.Events.Subscribe<PlaybackStateChangedEvent>(OnPlaybackStateChanged));
        _subscriptions.Add(context.Events.Subscribe<PlaybackPositionChangedEvent>(OnPositionChanged));

        // ── Register UI Contributions ────────────────────────
        context.RegisterSidebarPanel("osr2-sidebar", () => new SidebarView { DataContext = _sidebarVm });
        // context.RegisterRightPanel("osr2-axis-control", () => new AxisControlView { DataContext = _axisControlVm });
        // context.RegisterBottomPanel("osr2-visualizer", () => new VisualizerView { DataContext = _visualizerVm });
        // context.RegisterStatusBarItem("osr2-status", () => new StatusBarView { DataContext = _sidebarVm });
        // context.RegisterToolbarButtonHandler("osr2-quick-connect", OnQuickConnectClicked);

        context.RegisterFileIcons(new Dictionary<string, string>
        {
            { ".funscript", System.IO.Path.Combine(context.PluginDirectory, "Assets", "Icons", "funscript-stroke.png") }
        });

        // ── Load Saved Settings ──────────────────────────────
        LoadSettings();

        context.Logger.Info("OSR2+ Plugin activated", "OSR2+");
    }

    public void Deactivate()
    {
        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();

        _tcode?.Dispose();
        // _transport?.Dispose();

        _context?.Logger.Info("OSR2+ Plugin deactivated", "OSR2+");
    }

    // ── Event Handlers ───────────────────────────────────────

    private void OnVideoLoaded(VideoLoadedEvent e)
    {
        _context?.Logger.Debug($"Video loaded: {e.FilePath}", "OSR2+");

        if (_scriptLoader is null || _context is null) return;

        var logs = _scriptLoader.LoadScriptsForVideo(e.FilePath);
        foreach (var log in logs)
        {
            _context.Logger.Info(log, "OSR2+");
        }

        // TODO (VOSR-014): Update TCodeService with loaded scripts
        // TODO (VOSR-020+): Update AxisCardViewModels with script info
        // TODO (VOSR-032+): Update VisualizerViewModel with script data
    }

    private void OnVideoUnloaded(VideoUnloadedEvent e)
    {
        _context?.Logger.Debug("Video unloaded", "OSR2+");

        if (_scriptLoader is null || _context is null) return;

        var logs = _scriptLoader.ClearScripts();
        foreach (var log in logs)
        {
            _context.Logger.Info(log, "OSR2+");
        }

        // TODO (VOSR-014): Reset TCodeService
        // TODO (VOSR-020+): Reset AxisCardViewModels
        // TODO (VOSR-032+): Reset VisualizerViewModel
    }

    private void OnPlaybackStateChanged(PlaybackStateChangedEvent e)
    {
        _context?.Logger.Debug($"Playback state: {e.State}", "OSR2+");
        // TODO (VOSR-005): Start/stop TCode output thread
    }

    private void OnPositionChanged(PlaybackPositionChangedEvent e)
    {
        // TODO (VOSR-005): Update interpolation position
        // TODO (VOSR-025): Update visualizer position
    }

    // ── Settings ─────────────────────────────────────────────

    private void LoadSettings()
    {
        if (_context is null) return;

        // SidebarViewModel loads its own settings (connection mode, port, baud, rate, offset)
        // Future VMs will also load their own settings
        _context.Logger.Debug("Settings loaded via ViewModels", "OSR2+");
    }
}
