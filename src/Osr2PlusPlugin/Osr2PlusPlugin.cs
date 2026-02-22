using Vido.Core.Events;
using Vido.Core.Playback;
using Vido.Core.Plugin;
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

    // Services (created in future tickets)
    // private TCodeService? _tcode;
    // private InterpolationService? _interpolation;
    // private FunscriptParser? _parser;
    // private FunscriptMatcher? _matcher;
    // private ITransportService? _transport;

    // ViewModels (created in future tickets)
    // private SidebarViewModel? _sidebarVm;
    // private AxisControlViewModel? _axisControlVm;
    // private VisualizerViewModel? _visualizerVm;

    // Event subscriptions
    private readonly List<IDisposable> _subscriptions = new();

    public void Activate(IPluginContext context)
    {
        _context = context;

        // ── Create Services ──────────────────────────────────
        // TODO (VOSR-005+): Instantiate services
        // _interpolation = new InterpolationService();
        // _tcode = new TCodeService(_interpolation);
        // _parser = new FunscriptParser();
        // _matcher = new FunscriptMatcher();

        // ── Create ViewModels ────────────────────────────────
        // TODO (VOSR-017+): Instantiate ViewModels
        // _sidebarVm = new SidebarViewModel(_tcode, context.Settings);
        // _axisControlVm = new AxisControlViewModel(_tcode, _parser, _matcher, context.Settings);
        // _visualizerVm = new VisualizerViewModel(context.Settings);

        // ── Subscribe to Events ──────────────────────────────
        _subscriptions.Add(context.Events.Subscribe<VideoLoadedEvent>(OnVideoLoaded));
        _subscriptions.Add(context.Events.Subscribe<VideoUnloadedEvent>(OnVideoUnloaded));
        _subscriptions.Add(context.Events.Subscribe<PlaybackStateChangedEvent>(OnPlaybackStateChanged));
        _subscriptions.Add(context.Events.Subscribe<PlaybackPositionChangedEvent>(OnPositionChanged));

        // ── Register UI Contributions ────────────────────────
        // TODO (VOSR-017+): Replace placeholders with actual views
        context.RegisterSidebarPanel("osr2-sidebar", () => new SidebarView());
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

        // TODO (VOSR-005+): Dispose services
        // _tcode?.Dispose();
        // _transport?.Dispose();

        _context?.Logger.Info("OSR2+ Plugin deactivated", "OSR2+");
    }

    // ── Event Handlers ───────────────────────────────────────

    private void OnVideoLoaded(VideoLoadedEvent e)
    {
        _context?.Logger.Debug($"Video loaded: {e.FilePath}", "OSR2+");
        // TODO (VOSR-008): Match funscript files to video
        // TODO (VOSR-009): Parse matched funscripts
    }

    private void OnVideoUnloaded(VideoUnloadedEvent e)
    {
        _context?.Logger.Debug("Video unloaded", "OSR2+");
        // TODO (VOSR-009): Clear loaded funscripts
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

        var settings = _context.Settings;

        // Read persisted settings with defaults matching plugin.json
        var connectionMode = settings.Get("defaultConnectionMode", "UDP");
        var udpPort = settings.Get("defaultUdpPort", 7777);
        var baudRate = settings.Get("defaultBaudRate", "115200");
        var outputRate = settings.Get("tcodeOutputRate", 100);
        var globalOffset = settings.Get("globalFunscriptOffset", 0);
        var visualizerDuration = settings.Get("visualizerWindowDuration", "60");

        _context.Logger.Debug($"Settings loaded — mode={connectionMode}, rate={outputRate}Hz, offset={globalOffset}ms", "OSR2+");

        // TODO (VOSR-017+): Apply settings to ViewModels/Services
    }
}
