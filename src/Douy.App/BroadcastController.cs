using Douy;

namespace Douy.App;

public sealed class BroadcastController : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _encodeGate = new();
    private H264LiveEncoder? _encoder;
    private StreamServer? _server;
    private CancellationTokenSource? _pumpCts;
    private string? _videoPath;
    private int _generation;
    private bool _running;
    private int _framesInWindow;
    private long _windowStart = System.Diagnostics.Stopwatch.GetTimestamp();
    private long _lastPreviewStamp;
    private double _fps;

    public int Port => _server?.Port ?? 0;
    public bool IsRunning => _running;
    public string? VideoPath => _videoPath;

    public event EventHandler<PreviewFrame>? PreviewReady;
    public event EventHandler<double>? FpsChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? PhoneConnected;
    public event EventHandler? PhoneDisconnected;

    public async Task StartAsync(int port)
    {
        await _gate.WaitAsync();
        try
        {
            if (_running)
            {
                return;
            }

            var server = new StreamServer();
            server.ClientConnected += (_, name) =>
            {
                lock (_encodeGate)
                {
                    _encoder?.RequestKeyframe();
                }

                PhoneConnected?.Invoke(this, name);
            };
            server.ClientDisconnected += (_, _) => PhoneDisconnected?.Invoke(this, EventArgs.Empty);
            await server.StartAsync(port);
            _server = server;
            _running = true;
            StartPump();
            StatusChanged?.Invoke(this, "正在等待手机连接");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
            StatusChanged?.Invoke(this, "已停止");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UseVideo(string path)
    {
        _videoPath = path;
        if (_running)
        {
            StartPump();
        }

        StatusChanged?.Invoke(this, "已选择视频：" + Path.GetFileName(path));
    }

    public void UseTestPattern()
    {
        _videoPath = null;
        if (_running)
        {
            StartPump();
        }

        StatusChanged?.Invoke(this, _running ? "正在发送测试画面（H.264）" : "已改回测试画面");
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Increment(ref _generation);
        _pumpCts?.Cancel();
        lock (_encodeGate)
        {
            _encoder?.Dispose();
            _encoder = null;
        }

        if (_server != null)
        {
            await _server.DisposeAsync();
            _server = null;
        }

        _pumpCts?.Dispose();
        _pumpCts = null;
        _running = false;
    }

    private void StartPump()
    {
        _pumpCts?.Cancel();
        var cts = new CancellationTokenSource();
        _pumpCts = cts;
        var generation = Interlocked.Increment(ref _generation);
        var path = _videoPath;
        _ = Task.Run(() => RunPumpAsync(path, generation, cts.Token));
    }

    private async Task RunPumpAsync(string? path, int generation, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(path))
            {
                StatusChanged?.Invoke(this, "正在发送测试画面（H.264），手机连上后就能看到");
                await new TestPatternPump().Run(frame => Publish(frame, generation), cancellationToken);
                return;
            }

            StatusChanged?.Invoke(this, "正在播放 " + Path.GetFileName(path));
            await new VideoFilePump().Run(path, frame => Publish(frame, generation), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested)
        {
            _ = ex;
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }

            StatusChanged?.Invoke(this, "无法播放这个视频：" + ex.Message);
            if (!string.IsNullOrEmpty(path))
            {
                _videoPath = null;
                StartPump();
            }
        }
    }

    private void Publish(RawVideoFrame frame, int generation)
    {
        if (generation != Volatile.Read(ref _generation))
        {
            return;
        }

        H264AccessUnit? unit;
        lock (_encodeGate)
        {
            if (generation != Volatile.Read(ref _generation))
            {
                return;
            }

            _encoder ??= new H264LiveEncoder();
            if (frame.Index == 0 || (_server?.ConsumeDrop() ?? false))
            {
                _encoder.RequestKeyframe();
            }

            unit = _encoder.Encode(frame);
        }

        if (unit == null)
        {
            return;
        }

        _server?.PublishH264(unit.Value);
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        _framesInWindow++;
        var window = (now - _windowStart) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (window >= 0.5)
        {
            _fps = _framesInWindow / window;
            _framesInWindow = 0;
            _windowStart = now;
            FpsChanged?.Invoke(this, _fps);
        }

        var sincePreview = (now - _lastPreviewStamp) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (_lastPreviewStamp != 0 && sincePreview < 1.0 / 15.0)
        {
            return;
        }

        _lastPreviewStamp = now;
        var copy = new byte[frame.Bgr.Length];
        Buffer.BlockCopy(frame.Bgr, 0, copy, 0, copy.Length);
        PreviewReady?.Invoke(this, new PreviewFrame(copy, frame.Width, frame.Height, frame.Stride, _fps));
    }
}

public readonly record struct PreviewFrame(byte[] Bgr, int Width, int Height, int Stride, double Fps);
