namespace Douy;

public sealed class PacketReader
{
    private byte[] _buffer = new byte[64 * 1024];
    private int _count;

    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return;
        }

        Ensure(_count + data.Length);
        data.CopyTo(_buffer.AsSpan(_count));
        _count += data.Length;
    }

    public bool TryPop(out DouyPacket packet)
    {
        packet = default;
        while (_count >= DouyProtocol.HeaderSize)
        {
            if (!DouyProtocol.TryReadHeader(_buffer.AsSpan(0, DouyProtocol.HeaderSize), out var type, out var payloadLength))
            {
                Shift(1);
                continue;
            }

            var total = DouyProtocol.HeaderSize + payloadLength;
            if (_count < total)
            {
                return false;
            }

            var payload = new byte[payloadLength];
            if (payloadLength > 0)
            {
                Array.Copy(_buffer, DouyProtocol.HeaderSize, payload, 0, payloadLength);
            }

            Shift(total);
            packet = new DouyPacket(type, payload);
            return true;
        }

        return false;
    }

    private void Ensure(int size)
    {
        if (_buffer.Length >= size)
        {
            return;
        }

        var next = _buffer.Length;
        while (next < size)
        {
            next *= 2;
        }

        Array.Resize(ref _buffer, next);
    }

    private void Shift(int count)
    {
        _count -= count;
        if (_count > 0)
        {
            Buffer.BlockCopy(_buffer, count, _buffer, 0, _count);
        }
    }
}
