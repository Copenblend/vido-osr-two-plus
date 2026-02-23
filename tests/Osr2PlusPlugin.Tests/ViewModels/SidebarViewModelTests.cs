using System.ComponentModel;
using Moq;
using Osr2PlusPlugin.Models;
using Osr2PlusPlugin.Services;
using Osr2PlusPlugin.ViewModels;
using Vido.Core.Plugin;
using Xunit;

namespace Osr2PlusPlugin.Tests.ViewModels;

public class SidebarViewModelTests : IDisposable
{
    private readonly InterpolationService _interpolation = new();
    private readonly TCodeService _tcode;
    private readonly Mock<IPluginSettingsStore> _mockSettings;
    private readonly MockTransport _mockTransport;
    private readonly SidebarViewModel _sut;

    public SidebarViewModelTests()
    {
        _tcode = new TCodeService(_interpolation);
        _mockSettings = new Mock<IPluginSettingsStore>();

        // Set up default settings returns
        _mockSettings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("UDP");
        _mockSettings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(7777);
        _mockSettings.Setup(s => s.Get("defaultComPort", "")).Returns("");
        _mockSettings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(115200);
        _mockSettings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(100);
        _mockSettings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(0);

        _mockTransport = new MockTransport();

        _sut = new SidebarViewModel(_tcode, _mockSettings.Object);

        // Inject test transport factory
        _sut.TransportFactory = (mode, port, comPort, baud) => (_mockTransport, true);
    }

    public void Dispose()
    {
        _tcode.Dispose();
    }

    // ===== Default Values =====

    [Fact]
    public void Constructor_LoadsDefaultSettings()
    {
        Assert.Equal(ConnectionMode.UDP, _sut.SelectedMode);
        Assert.Equal(7777, _sut.UdpPort);
        Assert.Equal(115200, _sut.SelectedBaudRate);
        Assert.Equal(100, _sut.OutputRateHz);
        Assert.Equal(0, _sut.GlobalOffsetMs);
    }

    [Fact]
    public void Constructor_LoadsPersistedSettings()
    {
        var settings = new Mock<IPluginSettingsStore>();
        settings.Setup(s => s.Get("defaultConnectionMode", "UDP")).Returns("Serial");
        settings.Setup(s => s.Get("defaultUdpPort", 7777)).Returns(8888);
        settings.Setup(s => s.Get("defaultComPort", "")).Returns("COM5");
        settings.Setup(s => s.Get("defaultBaudRate", 115200)).Returns(9600);
        settings.Setup(s => s.Get("tcodeOutputRate", 100)).Returns(150);
        settings.Setup(s => s.Get("globalFunscriptOffset", 0)).Returns(-200);

        var vm = new SidebarViewModel(_tcode, settings.Object);

        Assert.Equal(ConnectionMode.Serial, vm.SelectedMode);
        Assert.Equal(8888, vm.UdpPort);
        Assert.Equal("COM5", vm.SelectedComPort);
        Assert.Equal(9600, vm.SelectedBaudRate);
        Assert.Equal(150, vm.OutputRateHz);
        Assert.Equal(-200, vm.GlobalOffsetMs);
    }

    // ===== Mode Selection =====

    [Fact]
    public void SelectedMode_IsUdpByDefault()
    {
        Assert.True(_sut.IsUdpMode);
        Assert.False(_sut.IsSerialMode);
    }

    [Fact]
    public void SelectedMode_SwitchToSerial_UpdatesVisibility()
    {
        _sut.SelectedMode = ConnectionMode.Serial;

        Assert.False(_sut.IsUdpMode);
        Assert.True(_sut.IsSerialMode);
    }

    [Fact]
    public void SelectedMode_PersistsOnChange()
    {
        _sut.SelectedMode = ConnectionMode.Serial;
        _mockSettings.Verify(s => s.Set("defaultConnectionMode", "Serial"), Times.Once);
    }

    [Fact]
    public void SelectedMode_NotifiesPropertyChanges()
    {
        var changed = new List<string>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        _sut.SelectedMode = ConnectionMode.Serial;

        Assert.Contains("SelectedMode", changed);
        Assert.Contains("IsUdpMode", changed);
        Assert.Contains("IsSerialMode", changed);
    }

    // ===== Connect / Disconnect =====

    [Fact]
    public void ConnectCommand_Connects_SetsIsConnected()
    {
        _sut.ConnectCommand.Execute(null);

        Assert.True(_sut.IsConnected);
        Assert.Equal("Disconnect", _sut.ConnectButtonText);
    }

    [Fact]
    public void ConnectCommand_WhenConnected_Disconnects()
    {
        _sut.ConnectCommand.Execute(null);
        Assert.True(_sut.IsConnected);

        _sut.ConnectCommand.Execute(null);
        Assert.False(_sut.IsConnected);
        Assert.Equal("Connect", _sut.ConnectButtonText);
    }

    [Fact]
    public void Connect_SetsTransportOnTCodeService()
    {
        _sut.Connect();
        Assert.Same(_mockTransport, _tcode.Transport);
    }

    [Fact]
    public void Disconnect_ClearsTransportOnTCodeService()
    {
        _sut.Connect();
        _sut.Disconnect();
        Assert.Null(_tcode.Transport);
    }

    [Fact]
    public void Connect_FactoryFails_DoesNotSetConnected()
    {
        _sut.TransportFactory = (_, _, _, _) => (null, false);

        _sut.Connect();

        Assert.False(_sut.IsConnected);
        Assert.Null(_tcode.Transport);
    }

    [Fact]
    public void Connect_PassesUdpModeParameters()
    {
        ConnectionMode? capturedMode = null;
        int capturedPort = 0;

        _sut.TransportFactory = (mode, port, _, _) =>
        {
            capturedMode = mode;
            capturedPort = port;
            return (_mockTransport, true);
        };

        _sut.SelectedMode = ConnectionMode.UDP;
        _sut.UdpPort = 9999;
        _sut.Connect();

        Assert.Equal(ConnectionMode.UDP, capturedMode);
        Assert.Equal(9999, capturedPort);
    }

    [Fact]
    public void Connect_PassesSerialModeParameters()
    {
        ConnectionMode? capturedMode = null;
        string? capturedCom = null;
        int capturedBaud = 0;

        _sut.TransportFactory = (mode, _, com, baud) =>
        {
            capturedMode = mode;
            capturedCom = com;
            capturedBaud = baud;
            return (_mockTransport, true);
        };

        _sut.SelectedMode = ConnectionMode.Serial;
        _sut.SelectedComPort = "COM7";
        _sut.SelectedBaudRate = 57600;
        _sut.Connect();

        Assert.Equal(ConnectionMode.Serial, capturedMode);
        Assert.Equal("COM7", capturedCom);
        Assert.Equal(57600, capturedBaud);
    }

    // ===== Settings Persistence =====

    [Fact]
    public void UdpPort_PersistsOnChange()
    {
        _sut.UdpPort = 9999;
        _mockSettings.Verify(s => s.Set("defaultUdpPort", 9999), Times.Once);
    }

    [Fact]
    public void UdpPort_ClampsToValidRange()
    {
        _sut.UdpPort = 0;
        Assert.Equal(1, _sut.UdpPort);

        _sut.UdpPort = 70000;
        Assert.Equal(65535, _sut.UdpPort);
    }

    [Fact]
    public void SelectedComPort_PersistsOnChange()
    {
        _sut.SelectedComPort = "COM5";
        _mockSettings.Verify(s => s.Set("defaultComPort", "COM5"), Times.Once);
    }

    [Fact]
    public void SelectedBaudRate_PersistsOnChange()
    {
        _sut.SelectedBaudRate = 9600;
        _mockSettings.Verify(s => s.Set("defaultBaudRate", 9600), Times.Once);
    }

    [Fact]
    public void OutputRateHz_PersistsAndPropagates()
    {
        _sut.OutputRateHz = 150;

        _mockSettings.Verify(s => s.Set("tcodeOutputRate", 150), Times.Once);
        Assert.Equal(150, _tcode.OutputRateHz);
    }

    [Fact]
    public void OutputRateHz_ClampsToValidRange()
    {
        _sut.OutputRateHz = 10;
        Assert.Equal(30, _sut.OutputRateHz);

        _sut.OutputRateHz = 500;
        Assert.Equal(200, _sut.OutputRateHz);
    }

    [Fact]
    public void GlobalOffsetMs_PersistsAndPropagates()
    {
        _sut.GlobalOffsetMs = -200;
        _mockSettings.Verify(s => s.Set("globalFunscriptOffset", -200), Times.Once);
    }

    [Fact]
    public void GlobalOffsetMs_ClampsToValidRange()
    {
        _sut.GlobalOffsetMs = -1000;
        Assert.Equal(-500, _sut.GlobalOffsetMs);

        _sut.GlobalOffsetMs = 1000;
        Assert.Equal(500, _sut.GlobalOffsetMs);
    }

    // ===== Refresh Ports =====

    [Fact]
    public void RefreshPortsCommand_DoesNotThrow()
    {
        // Should not throw even without real serial ports
        var ex = Record.Exception(() => _sut.RefreshPortsCommand.Execute(null));
        Assert.Null(ex);
    }

    // ===== Panel Commands =====

    [Fact]
    public void ShowAxisSettingsCommand_RaisesEvent()
    {
        bool fired = false;
        _sut.ShowAxisSettingsRequested += () => fired = true;

        _sut.ShowAxisSettingsCommand.Execute(null);

        Assert.True(fired);
    }

    [Fact]
    public void ShowVisualizerCommand_RaisesEvent()
    {
        bool fired = false;
        _sut.ShowVisualizerRequested += () => fired = true;

        _sut.ShowVisualizerCommand.Execute(null);

        Assert.True(fired);
    }

    // ===== Property Change Notifications =====

    [Theory]
    [InlineData(nameof(SidebarViewModel.UdpPort))]
    [InlineData(nameof(SidebarViewModel.OutputRateHz))]
    [InlineData(nameof(SidebarViewModel.GlobalOffsetMs))]
    public void Properties_RaisePropertyChanged(string propertyName)
    {
        var changed = new List<string>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        // Trigger a change on the property
        switch (propertyName)
        {
            case nameof(SidebarViewModel.UdpPort):
                _sut.UdpPort = 8888;
                break;
            case nameof(SidebarViewModel.OutputRateHz):
                _sut.OutputRateHz = 120;
                break;
            case nameof(SidebarViewModel.GlobalOffsetMs):
                _sut.GlobalOffsetMs = 50;
                break;
        }

        Assert.Contains(propertyName, changed);
    }

    [Fact]
    public void IsConnected_RaisesConnectButtonTextChanged()
    {
        var changed = new List<string>();
        _sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        _sut.Connect();

        Assert.Contains("IsConnected", changed);
        Assert.Contains("ConnectButtonText", changed);
    }

    // ===== ConnectButtonText =====

    [Fact]
    public void ConnectButtonText_IsConnect_WhenDisconnected()
    {
        Assert.Equal("Connect", _sut.ConnectButtonText);
    }

    [Fact]
    public void ConnectButtonText_IsDisconnect_WhenConnected()
    {
        _sut.Connect();
        Assert.Equal("Disconnect", _sut.ConnectButtonText);
    }

    // ===== Available Baud Rates =====

    [Fact]
    public void AvailableBaudRates_ContainsExpectedValues()
    {
        Assert.Contains(9600, _sut.AvailableBaudRates);
        Assert.Contains(115200, _sut.AvailableBaudRates);
        Assert.Contains(250000, _sut.AvailableBaudRates);
    }

    // ===== Mock Transport =====

    private class MockTransport : ITransportService
    {
        public bool IsConnected { get; set; } = true;
        public string? ConnectionLabel => "Mock";
        public List<string> SentMessages { get; } = new();

        public event Action<bool>? ConnectionChanged;
        public event Action<string>? ErrorOccurred;

        public void Send(string data) => SentMessages.Add(data);
        public void Disconnect() { IsConnected = false; }
        public void Dispose() { }

        public void SimulateConnectionDrop()
        {
            IsConnected = false;
            ConnectionChanged?.Invoke(false);
        }

        internal void SuppressWarnings()
        {
            ConnectionChanged?.Invoke(false);
            ErrorOccurred?.Invoke("");
        }
    }
}
