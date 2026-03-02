using System.IO.Ports;
using System.Text;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// Serial port transport for TCode output. Sends TCode commands
/// over a COM port to a physically connected OSR2+ device.
/// </summary>
public class SerialTransportService : ITransportService
{
    private readonly object _lock = new();
    private SerialPort? _port;

    /// <inheritdoc/>
    public bool IsConnected
    {
        get { lock (_lock) { return _port?.IsOpen ?? false; } }
    }

    /// <inheritdoc/>
    public string? ConnectionLabel
    {
        get { lock (_lock) { return _port?.IsOpen == true ? $"COM:{_port.PortName}" : null; } }
    }

    /// <inheritdoc/>
    public event Action<bool>? ConnectionChanged;

    /// <inheritdoc/>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// Returns the list of available serial port names on this machine.
    /// </summary>
    public static string[] ListPorts() => SerialPort.GetPortNames();

    /// <summary>
    /// Opens a serial connection on the specified port.
    /// </summary>
    /// <param name="portName">COM port name (e.g. "COM3").</param>
    /// <param name="baudRate">Baud rate (default 115200).</param>
    /// <returns>True if connection succeeded, false on error.</returns>
    public bool Connect(string portName, int baudRate = 115200)
    {
        try
        {
            Disconnect();

            lock (_lock)
            {
                _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500
                };

                _port.ErrorReceived += (_, e) =>
                {
                    ErrorOccurred?.Invoke($"Serial error: {e.EventType}");
                };

                _port.Open();
            }

            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Serial connect error: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Send(string data)
    {
        Span<byte> buffer = stackalloc byte[Encoding.UTF8.GetMaxByteCount(data.Length)];
        var written = Encoding.UTF8.GetBytes(data.AsSpan(), buffer);
        Send(buffer[..written]);
    }

    /// <inheritdoc/>
    public void Send(ReadOnlySpan<byte> data)
    {
        SerialPort? port;

        lock (_lock)
        {
            port = _port;
        }

        if (port?.IsOpen == true)
        {
            try
            {
                port.BaseStream.Write(data);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Serial send failed: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        bool wasConnected;

        lock (_lock)
        {
            wasConnected = _port != null;

            if (_port != null)
            {
                try
                {
                    if (_port.IsOpen)
                        _port.Close();
                }
                catch { /* Ignore close errors */ }

                _port.Dispose();
                _port = null;
            }
        }

        if (wasConnected)
        {
            ConnectionChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// Disposes the transport, disconnecting if connected.
    /// </summary>
    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
