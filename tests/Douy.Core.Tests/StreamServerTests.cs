using System.Net;
using System.Net.Sockets;
using Douy;

namespace Douy.Core.Tests;

public class StreamServerTests
{
    [Fact]
    public async Task ConnectedPhoneReceivesLatestFrame()
    {
        await using var server = new StreamServer();
        var names = new List<string>();
        server.ClientConnected += (_, name) =>
        {
            lock (names)
            {
                names.Add(name);
            }
        };

        await server.StartAsync(0);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        client.NoDelay = true;
        var stream = client.GetStream();
        await stream.WriteAsync(DouyProtocol.EncodeHello("iPhone"));

        var connected = await WaitUntilAsync(() =>
        {
            lock (names)
            {
                return names.Contains("iPhone");
            }
        });
        Assert.True(connected);

        var jpeg = new byte[] { 0xFF, 0xD8, 0x11, 0xFF, 0xD9 };
        server.PublishFrame(new VideoFrame(7, 720, 1280, 15, jpeg));
        var packet = await ReadPacketAsync(stream, TimeSpan.FromSeconds(3));

        Assert.Equal(DouyProtocol.TypeFrame, packet.Type);
        Assert.True(DouyProtocol.TryDecodeFrame(packet.Payload, out var frame));
        Assert.Equal(7u, frame.Index);
        Assert.Equal((ushort)720, frame.Width);
        Assert.Equal((ushort)1280, frame.Height);
        Assert.Equal(jpeg, frame.Jpeg);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (ready())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    private static async Task<DouyPacket> ReadPacketAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var reader = new PacketReader();
        var buffer = new byte[2048];
        while (!cancellation.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, cancellation.Token);
            Assert.NotEqual(0, read);
            reader.Append(buffer.AsSpan(0, read));
            if (reader.TryPop(out var packet))
            {
                return packet;
            }
        }

        throw new TimeoutException();
    }
}
