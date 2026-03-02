using Osr2PlusPlugin.Services;
using Xunit;

namespace Osr2PlusPlugin.Tests.Services;

public class SerialTransportServiceTests : IDisposable
{
    private readonly SerialTransportService _sut = new();

    public void Dispose()
    {
        _sut.Dispose();
    }

    // --- Initial state ---

    [Fact]
    public void InitialState_IsNotConnected()
    {
        Assert.False(_sut.IsConnected);
        Assert.Null(_sut.ConnectionLabel);
    }

    // --- ListPorts ---

    [Fact]
    public void ListPorts_ReturnsArrayWithoutThrowing()
    {
        // Static method — just verify it runs without error.
        // On CI/dev machines this may return an empty array.
        var ports = SerialTransportService.ListPorts();
        Assert.NotNull(ports);
    }

    // --- Connect error handling ---

    [Fact]
    public void Connect_InvalidPort_ReturnsFalseAndFiresError()
    {
        string? errorMsg = null;
        _sut.ErrorOccurred += msg => errorMsg = msg;

        var result = _sut.Connect("INVALID_PORT_NAME_XYZ", 115200);

        Assert.False(result);
        Assert.False(_sut.IsConnected);
        Assert.NotNull(errorMsg);
        Assert.Contains("Serial connect error", errorMsg);
    }

    [Fact]
    public void Connect_InvalidPort_DoesNotFireConnectionChanged()
    {
        bool eventFired = false;
        _sut.ConnectionChanged += _ => eventFired = true;

        _sut.Connect("INVALID_PORT_NAME_XYZ");

        Assert.False(eventFired);
    }

    // --- Disconnect ---

    [Fact]
    public void Disconnect_WhenNotConnected_DoesNotFireEvent()
    {
        bool eventFired = false;
        _sut.ConnectionChanged += _ => eventFired = true;

        _sut.Disconnect();

        Assert.False(eventFired);
    }

    [Fact]
    public void Disconnect_CalledTwice_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            _sut.Disconnect();
            _sut.Disconnect();
        });
        Assert.Null(ex);
    }

    // --- Send when not connected ---

    [Fact]
    public void Send_WhenNotConnected_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.Send("L0500\n"));
        Assert.Null(ex);
    }

    [Fact]
    public void SendSpan_WhenNotConnected_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.Send("L0500\n"u8.ToArray()));
        Assert.Null(ex);
    }

    // --- Dispose ---

    [Fact]
    public void Dispose_WhenNotConnected_DoesNotThrow()
    {
        var ex = Record.Exception(() => _sut.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            _sut.Dispose();
            _sut.Dispose();
        });
        Assert.Null(ex);
    }
}
