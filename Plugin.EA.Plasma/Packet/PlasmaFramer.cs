namespace Plugin.EA.Plasma.Packet;

/// <summary>
/// Reassembles whole <see cref="PlasmaPacket"/>s from a TCP byte stream, which
/// may split a packet across reads or carry several at once. One framer per
/// connection.
/// </summary>
public sealed class PlasmaFramer
{
    private byte[] _buffer = [];

    /// <summary>Adds received bytes and returns every complete packet now available, in order.</summary>
    public IEnumerable<PlasmaPacket> Receive(ReadOnlySpan<byte> incoming)
    {
        _buffer = _buffer.Length == 0 ? incoming.ToArray() : Concat(_buffer, incoming);

        var packets = new List<PlasmaPacket>();
        int offset = 0;
        while (true)
        {
            PlasmaPacket? packet = PlasmaPacket.TryRead(_buffer.AsSpan(offset), out int consumed);
            if (packet is null)
                break;
            packets.Add(packet);
            offset += consumed;
        }

        _buffer = offset == 0 ? _buffer : _buffer[offset..];
        return packets;
    }

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        byte[] result = new byte[a.Length + b.Length];
        a.CopyTo(result);
        b.CopyTo(result.AsSpan(a.Length));
        return result;
    }
}
