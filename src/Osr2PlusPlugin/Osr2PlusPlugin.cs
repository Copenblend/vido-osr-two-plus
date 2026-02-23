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
    private TCodeService? _tcode;
    private InterpolationService? _interpolation;

    // ViewModels
    private SidebarViewModel? _sidebarVm;
    private AxisControlViewModel? _axisControlVm;

    // Event subscriptions
    private readonly List<IDisposable> _subscriptions = new();

    public void Activate(IPluginContext context)
    {
        _context = context;

        // ── Create Services ──────────────────────────────────
        _parser = new FunscriptParser();
        _matcher = new FunscriptMatcher();
        _interpolation = new InterpolationService();
        _tcode = new TCodeService(_interpolation);

        // ── Create ViewModels ────────────────────────────────
        _sidebarVm = new SidebarViewModel(_tcode, context.Settings);
        _axisControlVm = new AxisControlViewModel(_tcode, context.Settings, _parser, _matcher);

        // Wire sidebar button to show/expand right panel
        _sidebarVm.ShowAxisSettingsRequested += () =>
        {
            context.RequestShowRightPanel("osr2-axis-control");
            context.Settings.Set("lastRightPanel", "osr2-axis-control");
        };

        // Wire device connection state to axis control
        _sidebarVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.IsConnected))
                _axisControlVm.SetDeviceConnected(_sidebarVm.IsConnected);
        };

        // Wire file dialog factory for manual script loading
        foreach (var card in _axisControlVm.AxisCards)
        {
            card.FileDialogFactory = () =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Funscript Files (*.funscript)|*.funscript|All Files (*.*)|*.*",
                    Title = $"Open Funscript for {card.AxisName} ({card.AxisId})"
                };
                return dialog.ShowDialog() == true ? dialog.FileName : null;
            };
        }

        // ── Subscribe to Events ──────────────────────────────
        _subscriptions.Add(context.Events.Subscribe<VideoLoadedEvent>(OnVideoLoaded));
        _subscriptions.Add(context.Events.Subscribe<VideoUnloadedEvent>(OnVideoUnloaded));
        _subscriptions.Add(context.Events.Subscribe<PlaybackStateChangedEvent>(OnPlaybackStateChanged));
        _subscriptions.Add(context.Events.Subscribe<PlaybackPositionChangedEvent>(OnPositionChanged));

        // ── Register UI Contributions ────────────────────────
        context.RegisterSidebarPanel("osr2-sidebar", () => new SidebarView { DataContext = _sidebarVm });
        context.RegisterRightPanel("osr2-axis-control", () => new AxisControlView { DataContext = _axisControlVm });

        context.RegisterFileIcons(new Dictionary<string, string>
        {
            { ".funscript", System.IO.Path.Combine(context.PluginDirectory, "Assets", "Icons", "funscript-stroke.png") }
        });

        // ── Load Saved Settings ──────────────────────────────
        LoadSettings();

        // ── Restore Right Panel ──────────────────────────────
        var lastPanel = context.Settings.Get("lastRightPanel", "");
        if (!string.IsNullOrEmpty(lastPanel))
            context.RequestShowRightPanel(lastPanel);

        context.Logger.Info("OSR2+ Plugin activated", "OSR2+");
    }

    public void Deactivate()
    {
        foreach (var sub in _subscriptions) sub.Dispose();
        _subscriptions.Clear();

        _tcode?.Dispose();

        _context?.Logger.Info("OSR2+ Plugin deactivated", "OSR2+");
    }

    // ── Event Handlers ───────────────────────────────────────

    private void OnVideoLoaded(VideoLoadedEvent e)
    {
        _context?.Logger.Debug($"Video loaded: {e.FilePath}", "OSR2+");

        if (_axisControlVm is null || _context is null) return;

        _axisControlVm.LoadScriptsForVideo(e.FilePath);
        _context.Logger.Info($"Scripts loaded for: {e.FilePath}", "OSR2+");
    }

    private void OnVideoUnloaded(VideoUnloadedEvent e)
    {
        _context?.Logger.Debug("Video unloaded", "OSR2+");

        if (_axisControlVm is null || _tcode is null) return;

        _axisControlVm.ClearScripts();
        _tcode.SetPlaying(false);
        _axisControlVm.SetVideoPlaying(false);
    }

    private void OnPlaybackStateChanged(PlaybackStateChangedEvent e)
    {
        _context?.Logger.Debug($"Playback state: {e.State}", "OSR2+");

        if (_tcode is null || _axisControlVm is null) return;

        var isPlaying = e.State == Vido.Core.Playback.PlaybackState.Playing;
        _tcode.SetPlaying(isPlaying);
        _axisControlVm.SetVideoPlaying(isPlaying);
    }

    private void OnPositionChanged(PlaybackPositionChangedEvent e)
    {
        _tcode?.SetTime(e.Position.TotalMilliseconds);
    }

    // ── Settings ─────────────────────────────────────────────

    private void LoadSettings()
    {
        // Each ViewModel loads its own settings from the IPluginSettingsStore
    }
}
