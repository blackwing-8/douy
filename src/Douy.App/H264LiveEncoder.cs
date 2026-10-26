using Douy;
using H264Sharp;

namespace Douy.App;

public sealed class H264LiveEncoder : IDisposable
{
    public const int Bitrate = 3_000_000;

    private H264Encoder? _encoder;
    private int _width;
    private int _height;
    private int _fps;
    private int _sinceKey = 10_000;
    private int _wantKey;

    public void RequestKeyframe()
    {
        Interlocked.Exchange(ref _wantKey, 1);
    }

    public H264AccessUnit? Encode(RawVideoFrame frame)
    {
        if (frame.Width < 2 || frame.Height < 2 || frame.Bgr.Length == 0)
        {
            return null;
        }

        var fps = Math.Clamp(frame.Fps, 1, 60);
        Ensure(frame.Width, frame.Height, fps);
        if (Interlocked.Exchange(ref _wantKey, 0) == 1 || _sinceKey >= fps)
        {
            _encoder!.ForceIntraFrame();
        }

        using var image = new RgbImage(ImageFormat.Bgr, frame.Width, frame.Height, frame.Stride, frame.Bgr);
        if (!_encoder!.Encode(image, out EncodedData[] layers) || layers == null || layers.Length == 0)
        {
            return null;
        }

        var data = layers.GetAllBytes();
        if (data.Length == 0)
        {
            return null;
        }

        var keyframe = false;
        foreach (var layer in layers)
        {
            if (layer.FrameType is FrameType.IDR or FrameType.I)
            {
                keyframe = true;
                break;
            }
        }

        if (!keyframe)
        {
            keyframe = ContainsIdr(data);
        }

        _sinceKey = keyframe ? 0 : _sinceKey + 1;
        return new H264AccessUnit(frame.Index, (ushort)frame.Width, (ushort)frame.Height, frame.PtsMilliseconds, keyframe, data);
    }

    public void Dispose()
    {
        _encoder?.Dispose();
        _encoder = null;
        _width = 0;
        _height = 0;
    }

    private void Ensure(int width, int height, int fps)
    {
        if (_encoder != null && _width == width && _height == height)
        {
            if (_fps != fps)
            {
                _encoder.SetTargetFps(fps);
                _fps = fps;
            }

            return;
        }

        _encoder?.Dispose();
        var encoder = new H264Encoder();
        var result = encoder.Initialize(width, height, Bitrate, fps, ConfigType.CameraBasic);
        if (result != 0)
        {
            encoder.Dispose();
            throw new InvalidOperationException("H.264 编码器初始化失败，代码 " + result + "。");
        }

        _encoder = encoder;
        _width = width;
        _height = height;
        _fps = fps;
        _sinceKey = 10_000;
    }

    private static bool ContainsIdr(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 4 < data.Length; i++)
        {
            int header;
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
            {
                header = i + 3;
            }
            else if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 0 && data[i + 3] == 1)
            {
                header = i + 4;
            }
            else
            {
                continue;
            }

            if (header < data.Length && (data[header] & 0x1F) == 5)
            {
                return true;
            }
        }

        return false;
    }
}

public readonly record struct RawVideoFrame(uint Index, int Width, int Height, int Stride, byte[] Bgr, ulong PtsMilliseconds, int Fps);
