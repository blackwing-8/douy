using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Douy;

namespace Douy.App;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly BroadcastController _controller = new();
    private int _previewBusy;
    private string _portText = DouyProtocol.DefaultPort.ToString(CultureInfo.InvariantCulture);
    private string _status = "还没开始。点「开始传输」，再在手机上连接。";
    private string _phone = "未连接";
    private string _source = "测试画面";
    private string _fpsText = "0 fps";
    private string _resolutionText = "—";
    private string? _videoPath;
    private bool _isRunning;
    private BitmapSource? _preview;

    public MainViewModel()
    {
        Addresses = LanAddress.GetIPv4();
        _controller.StatusChanged += (_, status) => Dispatch(() => Status = status);
        _controller.PhoneConnected += (_, name) => Dispatch(() => Phone = name);
        _controller.PhoneDisconnected += (_, _) => Dispatch(() =>
        {
            Phone = "未连接";
            Status = "手机已断开，继续等待";
        });
        _controller.FpsChanged += (_, fps) => Dispatch(() => FpsText = fps.ToString("0", CultureInfo.InvariantCulture) + " fps");
        _controller.PreviewReady += (_, frame) => DispatchPreview(frame);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> Addresses { get; private set; }

    public bool HasLanAddress => Addresses.Count > 0;

    public string PortText
    {
        get => _portText;
        set => Set(ref _portText, value);
    }

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public string Phone
    {
        get => _phone;
        set => Set(ref _phone, value);
    }

    public string Source
    {
        get => _source;
        set => Set(ref _source, value);
    }

    public string FpsText
    {
        get => _fpsText;
        set => Set(ref _fpsText, value);
    }

    public string ResolutionText
    {
        get => _resolutionText;
        set => Set(ref _resolutionText, value);
    }

    public string? VideoPath
    {
        get => _videoPath;
        private set
        {
            if (Set(ref _videoPath, value))
            {
                OnPropertyChanged(nameof(VideoFileName));
                OnPropertyChanged(nameof(HasVideo));
            }
        }
    }

    public string VideoFileName => string.IsNullOrEmpty(_videoPath) ? "还没有选择视频" : Path.GetFileName(_videoPath);

    public bool HasVideo => !string.IsNullOrEmpty(_videoPath);

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanEditPort));
                OnPropertyChanged(nameof(StartButtonText));
            }
        }
    }

    public bool CanEditPort => !IsRunning;

    public string StartButtonText => IsRunning ? "停止传输" : "开始传输";

    public BitmapSource? Preview
    {
        get => _preview;
        private set => Set(ref _preview, value);
    }

    public async Task ToggleAsync()
    {
        if (IsRunning)
        {
            await _controller.StopAsync();
            IsRunning = false;
            Phone = "未连接";
            FpsText = "0 fps";
            return;
        }

        if (!int.TryParse(PortText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            Status = "端口要填 1 到 65535 之间的数字。";
            return;
        }

        RefreshAddresses();
        if (!string.IsNullOrEmpty(VideoPath))
        {
            _controller.UseVideo(VideoPath);
            Source = VideoFileName;
        }
        else
        {
            Source = "测试画面";
        }

        try
        {
            await _controller.StartAsync(port);
        }
        catch (Exception ex)
        {
            Status = "启动失败：" + ex.Message;
            return;
        }

        IsRunning = true;
    }

    public void SelectVideo(string path)
    {
        VideoPath = path;
        Source = Path.GetFileName(path);
        _controller.UseVideo(path);
    }

    public void ClearVideo()
    {
        VideoPath = null;
        Source = "测试画面";
        _controller.UseTestPattern();
    }

    public void CopyAddress(string address)
    {
        var text = address + ":" + PortText.Trim();
        Clipboard.SetText(text);
        Status = "已复制 " + text + "，到手机兜鱼里粘贴地址。";
    }

    public async Task ShutdownAsync()
    {
        await _controller.DisposeAsync();
    }

    private void RefreshAddresses()
    {
        Addresses = LanAddress.GetIPv4();
        OnPropertyChanged(nameof(Addresses));
        OnPropertyChanged(nameof(HasLanAddress));
    }

    private void DispatchPreview(PreviewFrame frame)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _previewBusy, 1) == 1)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            try
            {
                var preview = CreatePreview(frame);
                if (preview != null)
                {
                    Preview = preview;
                }
                ResolutionText = frame.Width + " × " + frame.Height;
                if (frame.Fps > 0)
                {
                    FpsText = frame.Fps.ToString("0", CultureInfo.InvariantCulture) + " fps";
                }
            }
            finally
            {
                Interlocked.Exchange(ref _previewBusy, 0);
            }
        });
    }

    private static BitmapSource? CreatePreview(PreviewFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Stride <= 0 || frame.Bgr.Length < frame.Stride * frame.Height)
        {
            return null;
        }

        var tight = frame.Width * 3;
        var stride = ((tight + 3) / 4) * 4;
        var pixels = frame.Bgr;
        var usedStride = frame.Stride;
        if (usedStride != stride || pixels.Length < stride * frame.Height)
        {
            pixels = new byte[stride * frame.Height];
            var rowBytes = Math.Min(tight, frame.Stride);
            for (var y = 0; y < frame.Height; y++)
            {
                Buffer.BlockCopy(frame.Bgr, y * frame.Stride, pixels, y * stride, rowBytes);
            }

            usedStride = stride;
        }

        var image = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr24, null, pixels, usedStride);
        image.Freeze();
        return image;
    }

    private void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
