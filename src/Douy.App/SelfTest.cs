using System.Net;
using System.Net.Sockets;
using Douy;

namespace Douy.App;

public static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        await using var controller = new BroadcastController();
        try
        {
            await controller.StartAsync(0);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, controller.Port);
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var reader = new PacketReader();
            var buffer = new byte[64 * 1024];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!timeout.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read == 0)
                {
                    break;
                }

                reader.Append(buffer.AsSpan(0, read));
                while (reader.TryPop(out var packet))
                {
                    if (packet.Type != DouyProtocol.TypeFrame ||
                        !DouyProtocol.TryDecodeFrame(packet.Payload, out var frame) ||
                        frame.Jpeg.Length < 2 ||
                        frame.Jpeg[0] != 0xFF ||
                        frame.Jpeg[1] != 0xD8)
                    {
                        continue;
                    }

                    Console.WriteLine($"ok {frame.Width}x{frame.Height} {frame.Jpeg.Length}");
                    return 0;
                }
            }

            Console.Error.WriteLine("timeout");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
