using System.Buffers.Binary;
using System.Text;

namespace Douy;

/// <summary>
/// 电脑与手机之间的画面包。大端序。
/// 头部 10 字节：magic "DOUY"(4) + version(1) + type(1) + payloadLength(4)。
/// type 1 Hello：payload 为 UTF-8 设备名。
/// type 2 Frame：payload 为 frameIndex(4) + width(2) + height(2) + ptsMs(8) + JPEG。
/// type 3 Ping：payload 为空。
/// </summary>
public static class DouyProtocol
{
    public const uint Magic = 0x444F5559;
    public const byte Version = 1;
    public const byte TypeHello = 1;
    public const byte TypeFrame = 2;
    public const byte TypePing = 3;
    public const int HeaderSize = 10;
    public const int FramePrefixSize = 16;
    public const int MaxPayload = 8 * 1024 * 1024;
    public const int DefaultPort = 19527;

    public static byte[] EncodeHello(string deviceName)
    {
        var name = Encoding.UTF8.GetBytes(deviceName ?? string.Empty);
        return Pack(TypeHello, name);
    }

    public static byte[] EncodeFrame(VideoFrame frame)
    {
        var payload = new byte[FramePrefixSize + frame.Jpeg.Length];
        var span = payload.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, frame.Index);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], frame.Width);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], frame.Height);
        BinaryPrimitives.WriteUInt64BigEndian(span[8..], frame.PtsMilliseconds);
        frame.Jpeg.CopyTo(span[FramePrefixSize..]);
        return Pack(TypeFrame, payload);
    }

    public static bool TryDecodeFrame(ReadOnlySpan<byte> payload, out VideoFrame frame)
    {
        frame = default;
        if (payload.Length < FramePrefixSize)
        {
            return false;
        }

        var index = BinaryPrimitives.ReadUInt32BigEndian(payload);
        var width = BinaryPrimitives.ReadUInt16BigEndian(payload[4..]);
        var height = BinaryPrimitives.ReadUInt16BigEndian(payload[6..]);
        var pts = BinaryPrimitives.ReadUInt64BigEndian(payload[8..]);
        var jpeg = payload[FramePrefixSize..].ToArray();
        frame = new VideoFrame(index, width, height, pts, jpeg);
        return true;
    }

    public static string DecodeHello(ReadOnlySpan<byte> payload)
    {
        return Encoding.UTF8.GetString(payload);
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> header, out byte type, out int payloadLength)
    {
        type = 0;
        payloadLength = 0;
        if (header.Length < HeaderSize)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(header) != Magic || header[4] != Version)
        {
            return false;
        }

        type = header[5];
        var length = BinaryPrimitives.ReadUInt32BigEndian(header[6..]);
        if (length > MaxPayload)
        {
            return false;
        }

        payloadLength = (int)length;
        return true;
    }

    private static byte[] Pack(byte type, ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[HeaderSize + payload.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, Magic);
        span[4] = Version;
        span[5] = type;
        BinaryPrimitives.WriteUInt32BigEndian(span[6..], (uint)payload.Length);
        payload.CopyTo(span[HeaderSize..]);
        return buffer;
    }
}

public readonly record struct VideoFrame(uint Index, ushort Width, ushort Height, ulong PtsMilliseconds, byte[] Jpeg);

public readonly record struct DouyPacket(byte Type, byte[] Payload);
