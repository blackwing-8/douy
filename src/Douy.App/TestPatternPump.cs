using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Douy.App;

public sealed class TestPatternPump
{
    public const int Width = 720;
    public const int Height = 1280;
    public const int Fps = 30;

    public async Task Run(Action<RawVideoFrame> publish, CancellationToken cancellationToken)
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        using var fontTitle = new Font("Microsoft YaHei UI", 72, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fontBody = new Font("Microsoft YaHei UI", 36, FontStyle.Regular, GraphicsUnit.Pixel);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var buffer = Array.Empty<byte>();
        uint index = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var started = clock.Elapsed;
            Draw(bitmap, fontTitle, fontBody, clock.Elapsed, index);
            var bits = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var length = bits.Stride * bits.Height;
                if (buffer.Length != length)
                {
                    buffer = new byte[length];
                }

                Marshal.Copy(bits.Scan0, buffer, 0, length);
                var pts = (ulong)index * 1000 / Fps;
                publish(new RawVideoFrame(index, Width, Height, bits.Stride, buffer, pts, Fps));
            }
            finally
            {
                bitmap.UnlockBits(bits);
            }

            index++;
            var wait = TimeSpan.FromMilliseconds(1000.0 / Fps) - (clock.Elapsed - started);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }
        }
    }

    private static void Draw(Bitmap bitmap, Font titleFont, Font bodyFont, TimeSpan elapsed, uint index)
    {
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(18, 20, 26));
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var accent = new SolidBrush(Color.FromArgb(43, 182, 115));
        using var white = new SolidBrush(Color.FromArgb(244, 247, 251));
        using var muted = new SolidBrush(Color.FromArgb(154, 163, 178));
        var travel = (float)((Math.Sin(elapsed.TotalSeconds * 1.6) + 1) * 0.5);
        graphics.FillRectangle(accent, 72, 360 + travel * 640, 576, 28);
        graphics.DrawString("兜鱼", titleFont, white, new PointF(72, 140));
        graphics.DrawString("douy", bodyFont, accent, new PointF(72, 240));
        graphics.DrawString(elapsed.ToString(@"hh\:mm\:ss\.ff"), bodyFont, white, new PointF(72, 1040));
        graphics.DrawString("frame " + index, bodyFont, muted, new PointF(72, 1100));
    }
}
