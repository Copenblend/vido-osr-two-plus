using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Osr2PlusPlugin.Services;

/// <summary>
/// UDP transport for TCode output. Sends TCode commands as UTF-8
/// datagrams to localhost on a configurable port.
/// </summary>
public class UdpTransportService : ITransportService
{
    private readonly object _lock = new();
    private UdpClient? _client;
    private IPEndPoint? _endpoint;
    private int _port;

    /// <inheritdoc/>
    public bool IsConnected
    {
        get { lock (_lock) { return _client != null; } }
    }

    /// <inheritdoc/>
    public string? ConnectionLabel
    {
        get { lock (_lock) { return _client != null ? $"UDP:{_port}" : null; } }
    }

    /// <inheritdoc/>
    public event Action<bool>? ConnectionChanged;

    /// <inheritdoc/>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// Creates a UDP client targeting localhost on the specified port.
    /// </summary>
    /// <param name="port">The UDP port number to send datagrams to.</param>
    /// <returns>True if connection succeeded, false on error.</returns>
    public bool Connect(int port)
    {
        try
        {
            Disconnect();

            lock (_lock)
            {
                _port = port;
                _endpoint = new IPEndPoint(IPAddress.Loopback, port);
                _client = new UdpClient();
            }

            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"UDP connect error: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc/>
    public void Send(string data)
    {
        UdpClient? client;
        IPEndPoint? endpoint;

        lock (_lock)
        {
            client = _client;
            endpoint = _endpoint;
        }

        if (client != null && endpoint != null)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(data);
                client.Send(bytes, bytes.Length, endpoint);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"UDP send failed: {ex.Message}");
            }
        }
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        bool wasConnected;

        lock (_lock)
        {
            wasConnected = _client != null;

            if (_client != null)
            {
                try { _client.Close(); } catch { }
                _client.Dispose();
                _client = null;
                _endpoint = null;
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
