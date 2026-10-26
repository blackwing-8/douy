using System.Buffers.Binary;
using System.Text;

namespace Douy;

/// <summary>
/// 电脑与手机之间的画面包。大端序。
/// 头部 10 字节：magic "DOUY"(4) + version(1) + type(1) + payloadLength(4)。
/// type 1 Hello：payload 为 UTF-8 设备名。
/// type 2 Frame：旧的 JPEG 帧，frameIndex(4) + width(2) + height(2) + ptsMs(8) + JPEG。
/// type 3 Ping：payload 为空。
/// type 4 H264：frameIndex(4) + width(2) + height(2) + ptsMs(8) + flags(1) + Annex-B。
/// flags 最低位为 1 表示关键帧。关键帧里带 SPS 和 PPS。
/// </summary>
public static class DouyProtocol
{
    public const uint Magic = 0x444F5559;
    public const byte Version = 1;
    public const byte TypeHello = 1;
    public const byte TypeFrame = 2;
    public const byte TypePing = 3;
    public const byte TypeH264 = 4;
    public const int HeaderSize = 10;
    public const int FramePrefixSize = 16;
    public const int H264PrefixSize = 17;
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

    public static byte[] EncodeH264(H264AccessUnit unit)
    {
        var payload = new byte[H264PrefixSize + unit.Data.Length];
        var span = payload.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, unit.Index);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], unit.Width);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], unit.Height);
        BinaryPrimitives.WriteUInt64BigEndian(span[8..], unit.PtsMilliseconds);
        span[16] = unit.Keyframe ? (byte)1 : (byte)0;
        unit.Data.CopyTo(span[H264PrefixSize..]);
        return Pack(TypeH264, payload);
    }

    public static bool TryDecodeH264(ReadOnlySpan<byte> payload, out H264AccessUnit unit)
    {
        unit = default;
        if (payload.Length <= H264PrefixSize)
        {
            return false;
        }

        var index = BinaryPrimitives.ReadUInt32BigEndian(payload);
        var width = BinaryPrimitives.ReadUInt16BigEndian(payload[4..]);
        var height = BinaryPrimitives.ReadUInt16BigEndian(payload[6..]);
        var pts = BinaryPrimitives.ReadUInt64BigEndian(payload[8..]);
        var keyframe = (payload[16] & 1) != 0;
        var data = payload[H264PrefixSize..].ToArray();
        unit = new H264AccessUnit(index, width, height, pts, keyframe, data);
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

public readonly record struct H264AccessUnit(uint Index, ushort Width, ushort Height, ulong PtsMilliseconds, bool Keyframe, byte[] Data);

public readonly record struct DouyPacket(byte Type, byte[] Payload);
