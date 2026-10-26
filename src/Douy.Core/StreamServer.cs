using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Douy;

public sealed class StreamServer : IAsyncDisposable
{
    private readonly Channel<byte[]> _frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    private readonly object _clientGate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _sendTask;
    private TcpClient? _client;
    private int _disposed;

    public int Port { get; private set; }

    public event EventHandler<string>? ClientConnected;
    public event EventHandler? ClientDisconnected;

    public Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_listener != null)
        {
            throw new InvalidOperationException("传输已经开始。");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        var token = _cts.Token;
        _sendTask = SendLoop(token);
        _acceptTask = AcceptLoop(token);
        return Task.CompletedTask;
    }

    public void PublishFrame(VideoFrame frame)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        try
        {
            _frames.Writer.TryWrite(DouyProtocol.EncodeFrame(frame));
        }
        catch (ChannelClosedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _cts?.Cancel();
        try
        {
            _listener?.Stop();
        }
        catch (Exception)
        {
        }

        CloseCurrentClient(notify: false);
        _frames.Writer.TryComplete();

        var tasks = new List<Task>();
        if (_acceptTask != null)
        {
            tasks.Add(_acceptTask);
        }

        if (_sendTask != null)
        {
            tasks.Add(_sendTask);
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
        }

        _cts?.Dispose();
    }

    private async Task AcceptLoop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient incoming;
            try
            {
                incoming = await _listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }

            incoming.NoDelay = true;
            incoming.SendTimeout = 5000;
            Attach(incoming);
        }
    }

    private void Attach(TcpClient incoming)
    {
        TcpClient? previous;
        var endpoint = incoming.Client.RemoteEndPoint?.ToString() ?? "手机";
        lock (_clientGate)
        {
            previous = _client;
            _client = incoming;
        }

        try
        {
            previous?.Close();
        }
        catch (Exception)
        {
        }

        ClientConnected?.Invoke(this, endpoint);
        _ = ReadLoop(incoming);
    }

    private async Task ReadLoop(TcpClient client)
    {
        var reader = new PacketReader();
        var buffer = new byte[8192];
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer);
                }
                catch (Exception)
                {
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                reader.Append(buffer.AsSpan(0, read));
                while (reader.TryPop(out var packet))
                {
                    if (packet.Type != DouyProtocol.TypeHello)
                    {
                        continue;
                    }

                    var name = DouyProtocol.DecodeHello(packet.Payload).Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    lock (_clientGate)
                    {
                        if (!ReferenceEquals(_client, client))
                        {
                            return;
                        }
                    }

                    ClientConnected?.Invoke(this, name);
                }
            }
        }
        finally
        {
            Detach(client);
        }
    }

    private async Task SendLoop(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var packet in _frames.Reader.ReadAllAsync(cancellationToken))
            {
                var stream = CurrentStream();
                if (stream == null)
                {
                    continue;
                }

                try
                {
                    await stream.WriteAsync(packet, cancellationToken);
                }
                catch (Exception)
                {
                    CloseCurrentClient(notify: true);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChannelClosedException)
        {
        }
    }

    private NetworkStream? CurrentStream()
    {
        lock (_clientGate)
        {
            if (_client == null)
            {
                return null;
            }

            try
            {
                return _client.Connected ? _client.GetStream() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private void Detach(TcpClient client)
    {
        var removed = false;
        lock (_clientGate)
        {
            if (ReferenceEquals(_client, client))
            {
                _client = null;
                removed = true;
            }
        }

        try
        {
            client.Close();
        }
        catch (Exception)
        {
        }

        if (removed)
        {
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void CloseCurrentClient(bool notify)
    {
        TcpClient? current;
        lock (_clientGate)
        {
            current = _client;
            _client = null;
        }

        if (current == null)
        {
            return;
        }

        try
        {
            current.Close();
        }
        catch (Exception)
        {
        }

        if (notify)
        {
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}
