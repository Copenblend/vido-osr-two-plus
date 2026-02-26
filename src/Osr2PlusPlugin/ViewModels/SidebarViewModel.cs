using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Vido.Core.Events;
using Vido.Core.Plugin;
using Vido.Haptics;

namespace Osr2PlusPlugin.ViewModels;

/// <summary>
/// ViewModel for the sidebar panel. Manages connection settings (mode, port, baud),
/// output rate, global offset, and panel visibility commands. Persists settings
/// via <see cref="IPluginSettingsStore"/>. Publishes <see cref="HapticTransportStateEvent"/>
/// on connect/disconnect.
/// </summary>
public class SidebarViewModel : INotifyPropertyChanged
{
    private readonly TCodeService _tcode;
    private readonly IPluginSettingsStore _settings;
    private readonly IEventBus? _eventBus;

    // Connection state
    private ConnectionMode _selectedMode = ConnectionMode.UDP;
    private int _udpPort = 7777;
    private string _selectedComPort = "";
    private int _selectedBaudRate = 115200;
    private bool _isConnected;
    private bool _hasAttemptedConnection;
    private ITransportService? _transport;

    // Output settings
    private int _outputRateHz = 100;
    private int _globalOffsetMs;

    /// <summary>
    /// Factory for creating transport instances. Internal for test injection.
    /// Returns (transport, success).
    /// </summary>
    internal Func<ConnectionMode, int, string, int, (ITransportService? transport, bool success)> TransportFactory { get; set; }

    /// <summary>
    /// Factory for listing available COM ports. Internal for test injection.
    /// </summary>
    internal Func<string[]> PortLister { get; set; } = SerialTransportService.ListPorts;

    private static (ITransportService? transport, bool success) DefaultTransportFactory(
        ConnectionMode mode, int udpPort, string comPort, int baudRate)
    {
        if (mode == ConnectionMode.UDP)
        {
            var udp = new UdpTransportService();
            if (udp.Connect(udpPort))
                return (udp, true);
            udp.Dispose();
            return (null, false);
        }
        else
        {
            if (string.IsNullOrEmpty(comPort))
                return (null, false);
            var serial = new SerialTransportService();
            if (serial.Connect(comPort, baudRate))
                return (serial, true);
            serial.Dispose();
            return (null, false);
        }
    }

    /// <summary>
    /// Available baud rate values for the serial connection dropdown.
    /// </summary>
    public int[] AvailableBaudRates { get; } = [9600, 19200, 38400, 57600, 115200, 250000];

    /// <summary>
    /// Available COM ports detected on the system.
    /// </summary>
    public ObservableCollection<string> AvailableComPorts { get; } = new();

    /// <summary>
    /// Raised when the user requests the axis settings panel to be shown.
    /// </summary>
    public event Action? ShowAxisSettingsRequested;

    /// <summary>
    /// Raised when the user requests the funscript visualizer panel to be shown.
    /// </summary>
    public event Action? ShowVisualizerRequested;

    // ===== Properties =====

    /// <summary>Selected connection mode (UDP or Serial).</summary>
    public ConnectionMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (Set(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(IsUdpMode));
                OnPropertyChanged(nameof(IsSerialMode));
                _settings.Set("defaultConnectionMode", value.ToString());
            }
        }
    }

    /// <summary>True when UDP mode is selected (for conditional visibility).</summary>
    public bool IsUdpMode => _selectedMode == ConnectionMode.UDP;

    /// <summary>True when Serial mode is selected (for conditional visibility).</summary>
    public bool IsSerialMode => _selectedMode == ConnectionMode.Serial;

    /// <summary>UDP target port. Persisted.</summary>
    public int UdpPort
    {
        get => _udpPort;
        set
        {
            var clamped = Math.Clamp(value, 1, 65535);
            if (Set(ref _udpPort, clamped))
                _settings.Set("defaultUdpPort", clamped);
        }
    }

    /// <summary>Selected COM port name (e.g. "COM3"). Persisted.</summary>
    public string SelectedComPort
    {
        get => _selectedComPort;
        set
        {
            if (Set(ref _selectedComPort, value ?? ""))
                _settings.Set("defaultComPort", value ?? "");
        }
    }

    /// <summary>Selected serial baud rate. Persisted.</summary>
    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set
        {
            if (Set(ref _selectedBaudRate, value))
                _settings.Set("defaultBaudRate", value);
        }
    }

    /// <summary>Whether the transport is currently connected.</summary>
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (Set(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(IsNotConnected));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    /// <summary>True when not connected — used to disable connection settings while connected.</summary>
    public bool IsNotConnected => !_isConnected;

    /// <summary>Dynamic text for the connect/disconnect button.</summary>
    public string ConnectButtonText => _isConnected ? "Disconnect" : "Connect";

    /// <summary>
    /// Status bar display text reflecting connection state.
    /// Connected: "UDP:7777:Connected" or "COM:COM3:Connected".
    /// Disconnected: "UDP:7777:Disconnected" or "COM:Disconnected".
    /// Initial (no attempt): "OSR2+:Not Connected".
    /// </summary>
    public string StatusText
    {
        get
        {
            if (!_hasAttemptedConnection)
                return "OSR2+:Not Connected";

            if (_selectedMode == ConnectionMode.UDP)
                return _isConnected
                    ? $"UDP:{_udpPort}:Connected"
                    : $"UDP:{_udpPort}:Disconnected";

            return _isConnected
                ? $"COM:{_selectedComPort}:Connected"
                : "COM:Disconnected";
        }
    }

    /// <summary>TCode output rate in Hz (30–200). Persisted and propagated to TCodeService.</summary>
    public int OutputRateHz
    {
        get => _outputRateHz;
        set
        {
            var clamped = Math.Clamp(value, 30, 200);
            if (Set(ref _outputRateHz, clamped))
            {
                _tcode.SetOutputRate(clamped);
                _settings.Set("tcodeOutputRate", clamped);
            }
        }
    }

    /// <summary>Global funscript offset in ms (-500 to +500). Persisted and propagated to TCodeService.</summary>
    public int GlobalOffsetMs
    {
        get => _globalOffsetMs;
        set
        {
            var clamped = Math.Clamp(value, -500, 500);
            if (Set(ref _globalOffsetMs, clamped))
            {
                _tcode.SetOffset(clamped);
                _settings.Set("globalFunscriptOffset", clamped);
            }
        }
    }

    // ===== Commands =====

    /// <summary>Toggles connection: connects if disconnected, disconnects if connected.</summary>
    public ICommand ConnectCommand { get; }

    /// <summary>Refreshes the list of available COM ports.</summary>
    public ICommand RefreshPortsCommand { get; }

    /// <summary>Requests the axis settings panel to be shown.</summary>
    public ICommand ShowAxisSettingsCommand { get; }

    /// <summary>Requests the funscript visualizer panel to be shown.</summary>
    public ICommand ShowVisualizerCommand { get; }

    // ===== Constructor =====

    public SidebarViewModel(TCodeService tcode, IPluginSettingsStore settings, IEventBus? eventBus = null)
    {
        _tcode = tcode;
        _settings = settings;
        _eventBus = eventBus;

        // Initialize transport factory
        TransportFactory = DefaultTransportFactory;

        // Initialize commands
        ConnectCommand = new RelayCommand(ExecuteConnect);
        RefreshPortsCommand = new RelayCommand(ExecuteRefreshPorts);
        ShowAxisSettingsCommand = new RelayCommand(() => ShowAxisSettingsRequested?.Invoke());
        ShowVisualizerCommand = new RelayCommand(() => ShowVisualizerRequested?.Invoke());

        // Load persisted settings
        LoadSettings();

        // Initial COM port scan
        RefreshPorts();
    }

    // ===== Command Implementations =====

    private void ExecuteConnect()
    {
        if (_isConnected)
        {
            Disconnect();
        }
        else
        {
            Connect();
        }
    }

    internal void Connect()
    {
        _hasAttemptedConnection = true;

        // Dispose any existing transport
        _transport?.Dispose();
        _transport = null;

        var (transport, success) = TransportFactory(_selectedMode, _udpPort, _selectedComPort, _selectedBaudRate);
        if (!success || transport == null)
        {
            // Connection failed — still update status to show "Disconnected"
            OnPropertyChanged(nameof(StatusText));
            return;
        }

        _transport = transport;

        // Wire up transport events
        _transport.ConnectionChanged += OnTransportConnectionChanged;

        // Assign to TCodeService and start output
        _tcode.Transport = _transport;

        // Gradually move all axes to midpoint before starting normal output.
        // This ensures the device starts from a known safe center position
        // regardless of where the hardware was left by a previous session.
        _tcode.HomeAxes();

        _tcode.Start();

        IsConnected = true;
        PublishTransportState(true);
    }

    internal void Disconnect()
    {
        // Stop TCode output
        _tcode.StopTimer();
        _tcode.Transport = null;

        // Disconnect and dispose transport
        if (_transport != null)
        {
            _transport.ConnectionChanged -= OnTransportConnectionChanged;
            _transport.Disconnect();
            _transport.Dispose();
            _transport = null;
        }

        IsConnected = false;
        PublishTransportState(false);
    }

    private void OnTransportConnectionChanged(bool connected)
    {
        if (!connected)
        {
            // Transport dropped unexpectedly — clean up
            _tcode.StopTimer();
            _tcode.Transport = null;
            _transport = null;
            IsConnected = false;
            PublishTransportState(false);
        }
    }

    internal void ExecuteRefreshPorts()
    {
        RefreshPorts();
    }

    private void RefreshPorts()
    {
        AvailableComPorts.Clear();
        try
        {
            foreach (var port in PortLister())
                AvailableComPorts.Add(port);
        }
        catch
        {
            // ListPorts can fail on some systems — swallow
        }

        // Re-select the previously selected port if still available
        if (string.IsNullOrEmpty(_selectedComPort) && AvailableComPorts.Count > 0)
        {
            SelectedComPort = AvailableComPorts[0];
        }
    }

    // ===== Haptic Transport State Publishing =====

    /// <summary>
    /// Publishes a <see cref="HapticTransportStateEvent"/> on the event bus
    /// so other plugins can observe connect/disconnect.
    /// </summary>
    private void PublishTransportState(bool isConnected)
    {
        _eventBus?.Publish(new HapticTransportStateEvent
        {
            IsConnected = isConnected,
            ConnectionLabel = isConnected ? BuildConnectionLabel() : null,
        });
    }

    private string BuildConnectionLabel()
    {
        return _selectedMode == ConnectionMode.UDP
            ? $"UDP:{_udpPort}"
            : $"COM:{_selectedComPort}";
    }

    // ===== Settings Persistence =====

    private void LoadSettings()
    {
        var modeStr = _settings.Get("defaultConnectionMode", "UDP");
        if (Enum.TryParse<ConnectionMode>(modeStr, out var mode))
            _selectedMode = mode;

        _udpPort = _settings.Get("defaultUdpPort", 7777);
        _selectedComPort = _settings.Get("defaultComPort", "");
        _selectedBaudRate = _settings.Get("defaultBaudRate", 115200);
        _outputRateHz = _settings.Get("tcodeOutputRate", 100);
        _globalOffsetMs = _settings.Get("globalFunscriptOffset", 0);

        // Apply loaded values to TCodeService
        _tcode.SetOutputRate(_outputRateHz);
        _tcode.SetOffset(_globalOffsetMs);
    }

    /// <summary>
    /// Handles external setting changes (e.g. from the host Settings Panel).
    /// Re-reads the changed key and updates the backing field + service
    /// without re-saving (to avoid circular writes).
    /// </summary>
    internal void OnSettingChanged(string key)
    {
        switch (key)
        {
            case "defaultConnectionMode":
                var modeStr = _settings.Get("defaultConnectionMode", "UDP");
                if (Enum.TryParse<ConnectionMode>(modeStr, out var mode) && mode != _selectedMode)
                {
                    _selectedMode = mode;
                    OnPropertyChanged(nameof(SelectedMode));
                    OnPropertyChanged(nameof(IsUdpMode));
                    OnPropertyChanged(nameof(IsSerialMode));
                }
                break;
            case "defaultUdpPort":
                var port = _settings.Get("defaultUdpPort", 7777);
                if (port != _udpPort) { _udpPort = port; OnPropertyChanged(nameof(UdpPort)); }
                break;
            case "defaultBaudRate":
                var baud = _settings.Get("defaultBaudRate", 115200);
                if (baud != _selectedBaudRate) { _selectedBaudRate = baud; OnPropertyChanged(nameof(SelectedBaudRate)); }
                break;
            case "tcodeOutputRate":
                var rate = Math.Clamp(_settings.Get("tcodeOutputRate", 100), 30, 200);
                if (rate != _outputRateHz) { _outputRateHz = rate; _tcode.SetOutputRate(rate); OnPropertyChanged(nameof(OutputRateHz)); }
                break;
            case "globalFunscriptOffset":
                var offset = Math.Clamp(_settings.Get("globalFunscriptOffset", 0), -500, 500);
                if (offset != _globalOffsetMs) { _globalOffsetMs = offset; _tcode.SetOffset(offset); OnPropertyChanged(nameof(GlobalOffsetMs)); }
                break;
        }
    }

    // ===== INotifyPropertyChanged =====

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    // ===== Minimal ICommand Implementation =====

    private class RelayCommand : ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        internal void SuppressWarning() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
