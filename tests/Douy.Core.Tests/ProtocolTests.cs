using System.Text;
using Douy;

namespace Douy.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void FrameRoundTripKeepsJpeg()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9 };
        var encoded = DouyProtocol.EncodeFrame(new VideoFrame(9, 720, 1280, 321, jpeg));
        var reader = new PacketReader();
        reader.Append(encoded.AsSpan(0, 3));
        Assert.False(reader.TryPop(out _));
        reader.Append(encoded.AsSpan(3));

        Assert.True(reader.TryPop(out var packet));
        Assert.Equal(DouyProtocol.TypeFrame, packet.Type);
        Assert.True(DouyProtocol.TryDecodeFrame(packet.Payload, out var frame));
        Assert.Equal(9u, frame.Index);
        Assert.Equal((ushort)720, frame.Width);
        Assert.Equal((ushort)1280, frame.Height);
        Assert.Equal(321ul, frame.PtsMilliseconds);
        Assert.Equal(jpeg, frame.Jpeg);
        Assert.False(reader.TryPop(out _));
    }

    [Fact]
    public void HelloRoundTripKeepsDeviceName()
    {
        var encoded = DouyProtocol.EncodeHello("兜鱼的 iPhone");
        var reader = new PacketReader();
        foreach (var b in encoded)
        {
            reader.Append(new[] { b });
        }

        Assert.True(reader.TryPop(out var packet));
        Assert.Equal(DouyProtocol.TypeHello, packet.Type);
        Assert.Equal("兜鱼的 iPhone", DouyProtocol.DecodeHello(packet.Payload));
    }

    [Fact]
    public void GarbageBeforeMagicIsSkipped()
    {
        var encoded = DouyProtocol.EncodeHello("phone");
        var blob = new byte[encoded.Length + 4];
        blob[0] = 1;
        blob[1] = 2;
        blob[2] = 3;
        blob[3] = 4;
        encoded.CopyTo(blob.AsSpan(4));

        var reader = new PacketReader();
        reader.Append(blob);
        Assert.True(reader.TryPop(out var packet));
        Assert.Equal("phone", Encoding.UTF8.GetString(packet.Payload));
    }
}
