using System.Drawing;
using System.Drawing.Imaging;
using Douy;

namespace Douy.App;

public sealed class TestPatternPump
{
    public const int Width = 720;
    public const int Height = 1280;

    public async Task Run(Action<VideoFrame> publish, CancellationToken cancellationToken)
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        using var fontTitle = new Font("Microsoft YaHei UI", 72, FontStyle.Bold, GraphicsUnit.Pixel);
        using var fontBody = new Font("Microsoft YaHei UI", 36, FontStyle.Regular, GraphicsUnit.Pixel);
        var jpegCodec = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        uint index = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var jpeg = Draw(bitmap, fontTitle, fontBody, jpegCodec, clock.Elapsed, index);
            publish(new VideoFrame(index, Width, Height, (ulong)clock.ElapsedMilliseconds, jpeg));
            index++;
            await Task.Delay(33, cancellationToken);
        }
    }

    private static byte[] Draw(Bitmap bitmap, Font titleFont, Font bodyFont, ImageCodecInfo jpegCodec, TimeSpan elapsed, uint index)
    {
        using (var graphics = Graphics.FromImage(bitmap))
        {
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

        using var stream = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 75L);
        bitmap.Save(stream, jpegCodec, parameters);
        return stream.ToArray();
    }
}
