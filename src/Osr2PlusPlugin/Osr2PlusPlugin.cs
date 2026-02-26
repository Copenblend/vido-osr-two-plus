using System.Reflection;
using System.Runtime.InteropServices;
using Vido.Core.Events;
using Vido.Core.Playback;
using Vido.Core.Plugin;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Osr2PlusPlugin.ViewModels;
using Osr2PlusPlugin.Views;
using Vido.Haptics;

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
    private VisualizerViewModel? _visualizerVm;
    private BeatBarViewModel? _beatBarVm;

    // Beat bar
    private BeatDetectionService? _beatDetection;

    // Event subscriptions
    private readonly List<IDisposable> _subscriptions = new();

    // Speed ratio tracking — avoid redundant SetPlaybackSpeed calls
    private double _lastSpeedRatio = 1.0;

    // Assembly resolver for plugin dependencies (SkiaSharp etc.)
    private ResolveEventHandler? _assemblyResolveHandler;

    public void Activate(IPluginContext context)
    {
        _context = context;

        // ── Register assembly resolver for plugin dependencies ───
        // The host loads the plugin DLL from a byte array (no probing path),
        // so .NET cannot locate dependent assemblies (SkiaSharp, etc.)
        // automatically. This handler resolves them from the plugin directory.
        _assemblyResolveHandler = (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (name is null) return null;

            // Prefer RID-specific assembly (e.g. runtimes/win/lib/net8.0/) so that
            // platform-dependent packages like System.IO.Ports load the real Windows
            // implementation instead of the portable stub that throws
            // PlatformNotSupportedException.
            var ridPath = System.IO.Path.Combine(
                context.PluginDirectory, "runtimes", "win", "lib", "net8.0", $"{name}.dll");
            if (System.IO.File.Exists(ridPath))
                return Assembly.LoadFrom(ridPath);

            var dllPath = System.IO.Path.Combine(context.PluginDirectory, $"{name}.dll");
            if (System.IO.File.Exists(dllPath))
                return Assembly.LoadFrom(dllPath);
            return null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += _assemblyResolveHandler;

        // Pre-load the libSkiaSharp native library from the plugin's runtimes
        // folder so P/Invoke can find it. AddDllDirectory alone doesn't work
        // because default LoadLibrary doesn't honour it without LOAD_LIBRARY_SEARCH_DEFAULT_DIRS.
        PreloadNativeSkiaSharp(context);

        // ── Create Services ──────────────────────────────────
        _parser = new FunscriptParser();
        _matcher = new FunscriptMatcher();
        _interpolation = new InterpolationService();
        _tcode = new TCodeService(_interpolation);

        // ── Create ViewModels ────────────────────────────────
        _sidebarVm = new SidebarViewModel(_tcode, context.Settings, context.Events);
        _axisControlVm = new AxisControlViewModel(_tcode, context.Settings, _parser, _matcher);
        _visualizerVm = new VisualizerViewModel(context.Settings);

        // Wire sidebar button to show/expand right panel
        _sidebarVm.ShowAxisSettingsRequested += () =>
        {
            context.RequestShowRightPanel("osr2-axis-control");
            context.Settings.Set("lastRightPanel", "osr2-axis-control");
        };

        // Wire sidebar button to show/expand bottom panel
        _sidebarVm.ShowVisualizerRequested += () =>
        {
            context.RequestShowBottomPanel("osr2-visualizer");
        };

        // Wire device connection state to axis control
        _sidebarVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SidebarViewModel.IsConnected))
            {
                _axisControlVm.SetDeviceConnected(_sidebarVm.IsConnected);
                context.SetToolbarButtonHighlight("osr2-quick-connect", _sidebarVm.IsConnected);
            }

            // Push status bar text updates to the host
            if (e.PropertyName == nameof(SidebarViewModel.StatusText))
            {
                context.UpdateStatusBarItem("osr2-status", _sidebarVm.StatusText);
            }
        };

        // Wire script changes to visualizer
        _axisControlVm.ScriptsChanged += scripts => _visualizerVm.SetLoadedAxes(scripts);

        // ── Beat Bar ─────────────────────────────────────────
        _beatDetection = new BeatDetectionService();
        _beatBarVm = new BeatBarViewModel(context.Settings, _beatDetection);

        // Wire script changes to beat bar (use L0 axis)
        _axisControlVm.ScriptsChanged += scripts =>
        {
            if (scripts.TryGetValue("L0", out var l0Script))
                _beatBarVm.LoadBeats(l0Script);
            else
                _beatBarVm.ClearBeats();

            // Ensure overlay visibility matches current mode.
            // ModeChanged only fires on mode transitions, so if the mode was
            // persisted as OnPeak/OnValley the overlay would stay hidden after
            // a video load or resume.
            context.ToggleControlBarOverlay("beat-bar", _beatBarVm.IsActive);
        };

        // Wire mode change to overlay visibility
        _beatBarVm.ModeChanged += mode =>
        {
            context.ToggleControlBarOverlay("beat-bar", mode != BeatBarMode.Off);
        };

        // ── Haptic Event Bus Wiring ──────────────────────────

        // Subscribe: external beat source registration → BeatBarViewModel
        _subscriptions.Add(context.Events.Subscribe<ExternalBeatSourceRegistration>(
            reg => _beatBarVm.OnBeatSourceRegistration(reg)));

        // Subscribe: external beat events → BeatBarViewModel
        _subscriptions.Add(context.Events.Subscribe<ExternalBeatEvent>(
            evt => _beatBarVm.OnExternalBeatEvent(evt)));

        // Subscribe: funscript suppression → AxisControlViewModel
        _subscriptions.Add(context.Events.Subscribe<SuppressFunscriptEvent>(
            evt => _axisControlVm.OnSuppressFunscript(evt)));

        // Subscribe: external axis positions → TCodeService
        _subscriptions.Add(context.Events.Subscribe<ExternalAxisPositionsEvent>(
            evt => _tcode.SetExternalPositions(evt.Positions)));

        // Publish: script changes → HapticScriptsChangedEvent
        _axisControlVm.ScriptsChanged += scripts =>
        {
            context.Events.Publish(new HapticScriptsChangedEvent
            {
                HasAnyScripts = scripts.Count > 0,
                AxisScriptLoaded = scripts.Keys.ToDictionary(k => k, _ => true),
            });
        };

        // Publish: axis config changes → HapticAxisConfigEvent
        PublishAxisConfig(context);
        _axisControlVm.AxisConfigChanged += () => PublishAxisConfig(context);

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
        context.RegisterBottomPanel("osr2-visualizer", () => new VisualizerView { DataContext = _visualizerVm });
        context.RegisterStatusBarItem("osr2-status", () => _sidebarVm.StatusText);
        context.RegisterControlBarItem("beat-bar",
            () => new BeatBarComboBox { DataContext = _beatBarVm },
            () => new BeatBarOverlay { DataContext = _beatBarVm });
        context.RegisterToolbarButtonHandler("osr2-quick-connect", OnQuickConnectClicked);

        var iconsDir = System.IO.Path.Combine(context.PluginDirectory, "Assets", "Icons");
        context.RegisterFileIcons(new Dictionary<string, string>
        {
            { ".funscript", System.IO.Path.Combine(iconsDir, "funscript-stroke.png") },
            { ".twist.funscript", System.IO.Path.Combine(iconsDir, "funscript-twist.png") },
            { ".roll.funscript", System.IO.Path.Combine(iconsDir, "funscript-roll.png") },
            { ".pitch.funscript", System.IO.Path.Combine(iconsDir, "funscript-pitch.png") }
        });

        // ── Load Saved Settings ──────────────────────────────
        // Each ViewModel loads its own settings during construction.
        // Subscribe to SettingChanged so external changes (e.g. host
        // Settings Panel) propagate to VMs and services in real-time.
        context.Settings.SettingChanged += OnSettingChanged;

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

        // Unsubscribe from settings changes
        if (_context is not null)
            _context.Settings.SettingChanged -= OnSettingChanged;

        // Remove the assembly resolver
        if (_assemblyResolveHandler is not null)
        {
            AppDomain.CurrentDomain.AssemblyResolve -= _assemblyResolveHandler;
            _assemblyResolveHandler = null;
        }

        _tcode?.Dispose();

        _context?.Logger.Info("OSR2+ Plugin deactivated", "OSR2+");
    }

    // ── Event Handlers ───────────────────────────────────────

    private void OnVideoLoaded(VideoLoadedEvent e)
    {
        try
        {
            _context?.Logger.Debug($"Video loaded: {e.FilePath}", "OSR2+");

            if (_axisControlVm is null || _context is null) return;

            _axisControlVm.LoadScriptsForVideo(e.FilePath);

            // Sync speed ratio from the video engine in case it was changed
            // before this video was loaded (e.g. user set 2× and opened a file)
            SyncSpeedRatio();

            _context.Logger.Info($"Scripts loaded for: {e.FilePath}", "OSR2+");
        }
        catch (Exception ex)
        {
            _context?.Logger.Error($"VideoLoaded handler error: {ex.Message}", "OSR2+");
        }
    }

    private void OnVideoUnloaded(VideoUnloadedEvent e)
    {
        try
        {
            _context?.Logger.Debug("Video unloaded", "OSR2+");

            if (_axisControlVm is null || _tcode is null) return;

            _axisControlVm.ClearScripts();
            _beatBarVm?.ClearBeats();
            _tcode.SetPlaying(false);
            _axisControlVm.SetVideoPlaying(false);
            _visualizerVm?.ClearAxes();
        }
        catch (Exception ex)
        {
            _context?.Logger.Error($"VideoUnloaded handler error: {ex.Message}", "OSR2+");
        }
    }

    private void OnPlaybackStateChanged(PlaybackStateChangedEvent e)
    {
        try
        {
            _context?.Logger.Debug($"Playback state: {e.State}", "OSR2+");

            if (_tcode is null || _axisControlVm is null) return;

            var isPlaying = e.State == Vido.Core.Playback.PlaybackState.Playing;
            _tcode.SetPlaying(isPlaying);
            _axisControlVm.SetVideoPlaying(isPlaying);
        }
        catch (Exception ex)
        {
            _context?.Logger.Error($"PlaybackStateChanged handler error: {ex.Message}", "OSR2+");
        }
    }

    private void OnPositionChanged(PlaybackPositionChangedEvent e)
    {
        try
        {
            _tcode?.SetTime(e.Position.TotalMilliseconds);
            _visualizerVm?.UpdateTime(e.Position.TotalSeconds);
            _beatBarVm?.UpdateTime(e.Position.TotalMilliseconds);

            // Poll speed ratio from the video engine on each position tick.
            // There is no dedicated SpeedRatioChanged event, so we check here
            // (~60 Hz) and only call SetPlaybackSpeed when the value changes.
            SyncSpeedRatio();
        }
        catch (Exception ex)
        {
            _context?.Logger.Error($"PositionChanged handler error: {ex.Message}", "OSR2+");
        }
    }

    /// <summary>
    /// Reads the current speed ratio from the video engine and forwards it
    /// to <see cref="TCodeService.SetPlaybackSpeed"/> if it has changed.
    /// </summary>
    private void SyncSpeedRatio()
    {
        if (_context is null || _tcode is null) return;

        var speed = _context.VideoEngine.SpeedRatio;
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        if (speed != _lastSpeedRatio)
        {
            _lastSpeedRatio = speed;
            _tcode.SetPlaybackSpeed((float)speed);
            _context.Logger.Debug($"Playback speed updated: {speed:F2}×", "OSR2+");
        }
    }

    // ── Settings ─────────────────────────────────────────────

    /// <summary>
    /// Handles external setting changes from the host Settings Panel.
    /// Dispatches to the owning ViewModel so the UI and services stay in sync.
    /// </summary>
    private void OnSettingChanged(string key)
    {
        try
        {
            _sidebarVm?.OnSettingChanged(key);
            _axisControlVm?.OnSettingChanged(key);
            _visualizerVm?.OnSettingChanged(key);
            _beatBarVm?.OnSettingChanged(key);
        }
        catch (Exception ex)
        {
            _context?.Logger.Error($"SettingChanged handler error: {ex.Message}", "OSR2+");
        }
    }

    // ── Quick Connect ────────────────────────────────────────

    private void OnQuickConnectClicked()
    {
        try
        {
            _sidebarVm?.ConnectCommand.Execute(null);
        }
        catch (Exception ex)
        {
            _context?.Logger.Error($"Quick connect error: {ex.Message}", "OSR2+");
        }
    }

    // ── Haptic Axis Config Publishing ──────────────────────

    /// <summary>
    /// Publishes the current axis configurations as a <see cref="HapticAxisConfigEvent"/>
    /// so other plugins can read axis constraints.
    /// </summary>
    private void PublishAxisConfig(IPluginContext context)
    {
        if (_axisControlVm == null) return;

        var snapshots = _axisControlVm.AxisCards.Select(card => new HapticAxisSnapshot
        {
            Id = card.AxisId,
            Min = card.Min,
            Max = card.Max,
            Enabled = card.Enabled,
        }).ToList();

        context.Events.Publish(new HapticAxisConfigEvent { Axes = snapshots });
    }

    // ── Native library loading ─────────────────────────────

    /// <summary>
    /// Eagerly loads the libSkiaSharp native library from the plugin's
    /// <c>runtimes/{rid}/native</c> folder using <see cref="NativeLibrary.Load"/>.
    /// This ensures the DLL is already in the process before SkiaSharp's
    /// P/Invoke calls attempt to find it via the default search order
    /// (which doesn't include the plugin directory).
    /// </summary>
    private static void PreloadNativeSkiaSharp(IPluginContext context)
    {
        var rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64   => "win-x64",
            Architecture.Arm64 => "win-arm64",
            Architecture.X86   => "win-x86",
            _                  => "win-x64"
        };

        var nativePath = System.IO.Path.Combine(
            context.PluginDirectory, "runtimes", rid, "native", "libSkiaSharp.dll");

        if (!System.IO.File.Exists(nativePath))
        {
            context.Logger.Warning(
                $"Native library not found at '{nativePath}'", "OSR2+");
            return;
        }

        try
        {
            NativeLibrary.Load(nativePath);
            context.Logger.Debug(
                $"Pre-loaded native library: {nativePath}", "OSR2+");
        }
        catch (Exception ex)
        {
            context.Logger.Error(
                $"Failed to pre-load libSkiaSharp: {ex.Message}", "OSR2+");
        }
    }
}
