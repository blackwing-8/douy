using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Douy.App;

public sealed class VideoFilePump
{
    private const int MaxEdge = 1280;

    public async Task Run(string path, Action<RawVideoFrame> publish, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("找不到视频文件。", path);
        }

        var capture = new VideoCapture();
        var openedPath = path;
        var buffer = Array.Empty<byte>();
        try
        {
            openedPath = Open(capture, path);
            var fps = capture.Get(VideoCaptureProperties.Fps);
            if (fps < 1 || fps > 120)
            {
                fps = 30;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            using var raw = new Mat();
            using var fitted = new Mat();
            uint index = 0;
            var failures = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!capture.Read(raw) || raw.Empty())
                {
                    failures++;
                    if (failures > 2)
                    {
                        throw new InvalidOperationException("读到视频结尾后无法重新开始。");
                    }

                    capture.Set(VideoCaptureProperties.PosFrames, 0);
                    if (!capture.Read(raw) || raw.Empty())
                    {
                        capture.Release();
                        Open(capture, openedPath);
                    }

                    clock.Restart();
                    index = 0;
                    continue;
                }

                failures = 0;
                var target = TimeSpan.FromSeconds(index / fps);
                if (clock.Elapsed - target > TimeSpan.FromMilliseconds(80))
                {
                    index++;
                    continue;
                }

                var source = Fit(raw, fitted);
                buffer = CopyBgr(source, buffer);
                var pts = (ulong)(index * 1000.0 / fps);
                publish(new RawVideoFrame(index, source.Width, source.Height, (int)source.Step(), buffer, pts, (int)Math.Round(fps)));
                index++;

                var wait = TimeSpan.FromSeconds(index / fps) - clock.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancellationToken);
                }
            }
        }
        finally
        {
            capture.Dispose();
            if (!string.Equals(openedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(openedPath);
            }
        }
    }

    private static byte[] CopyBgr(Mat source, byte[] reuse)
    {
        var stride = (int)source.Step();
        var length = stride * source.Rows;
        if (reuse.Length != length)
        {
            reuse = new byte[length];
        }

        Marshal.Copy(source.Data, reuse, 0, length);
        return reuse;
    }

    private static string Open(VideoCapture capture, string path)
    {
        capture.Open(path);
        if (capture.IsOpened())
        {
            return path;
        }

        capture.Release();
        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            extension = ".mp4";
        }

        var temp = Path.Combine(Path.GetTempPath(), "douy-" + Guid.NewGuid().ToString("N") + extension);
        File.Copy(path, temp, overwrite: true);
        capture.Open(temp);
        if (!capture.IsOpened())
        {
            TryDelete(temp);
            throw new InvalidOperationException("打不开这个视频，请换一个 mp4 文件。");
        }

        return temp;
    }

    private static Mat Fit(Mat source, Mat destination)
    {
        var width = source.Width;
        var height = source.Height;
        var longEdge = Math.Max(width, height);
        var scale = longEdge > MaxEdge ? MaxEdge / (double)longEdge : 1;
        var fittedWidth = Math.Max(2, (int)Math.Round(width * scale)) / 2 * 2;
        var fittedHeight = Math.Max(2, (int)Math.Round(height * scale)) / 2 * 2;
        if (fittedWidth == width && fittedHeight == height)
        {
            return source;
        }

        Cv2.Resize(source, destination, new Size(fittedWidth, fittedHeight), 0, 0, InterpolationFlags.Area);
        return destination;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}
