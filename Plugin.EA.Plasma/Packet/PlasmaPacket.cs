using System.Buffers.Binary;
using System.Text;

namespace Plugin.EA.Plasma.Packet;

/// <summary>
/// One Plasma wire packet, used by both FESL and Theater. The 12-byte header is
/// a four-character type (<c>fsys</c>, <c>acct</c>, <c>CONN</c>, <c>GDAT</c>…), a
/// 32-bit word whose top byte is the transmission type and whose low 24 bits are
/// the packet id, and a 32-bit total length. The body is a run of
/// <c>key=value\n</c> text. This is our own reader/writer, modelled from the
/// documented format so it round-trips every field; nothing is copied from an
/// existing emulator.
/// </summary>
public sealed class PlasmaPacket
{
    public const int HeaderSize = 12;

    public PlasmaPacket(string type, PlasmaTransmission transmission, uint id, IDictionary<string, string>? fields = null)
    {
        Type = type;
        Transmission = transmission;
        Id = id;
        Fields = fields is null ? new Dictionary<string, string>() : new Dictionary<string, string>(fields);
    }

    /// <summary>Four-character packet type, e.g. <c>fsys</c> or <c>CONN</c>.</summary>
    public string Type { get; }

    public PlasmaTransmission Transmission { get; }

    /// <summary>Packet id; replies echo the request's id so the client can pair them.</summary>
    public uint Id { get; }

    public Dictionary<string, string> Fields { get; }

    /// <summary>Convenience accessor for the <c>TXN</c> field FESL uses to name the transaction.</summary>
    public string Txn => Fields.GetValueOrDefault("TXN", "");

    public string? this[string key]
    {
        get => Fields.GetValueOrDefault(key);
        set { if (value is not null) Fields[key] = value; }
    }

    /// <summary>
    /// Reads one packet from the front of <paramref name="buffer"/>. Returns
    /// null when fewer than a whole packet's bytes are present yet;
    /// <paramref name="consumed"/> is how many bytes the packet used.
    /// </summary>
    public static PlasmaPacket? TryRead(ReadOnlySpan<byte> buffer, out int consumed)
    {
        consumed = 0;
        if (buffer.Length < HeaderSize)
            return null;

        string type = Encoding.ASCII.GetString(buffer[..4]);
        uint word = BinaryPrimitives.ReadUInt32BigEndian(buffer[4..8]);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(buffer[8..12]);

        if (length < HeaderSize || buffer.Length < length)
            return null;

        var transmission = (PlasmaTransmission)(word >> 24);
        uint id = word & 0x00ff_ffff;

        ReadOnlySpan<byte> body = buffer[HeaderSize..(int)length];
        var fields = PlasmaFields.Parse(body);

        consumed = (int)length;
        return new PlasmaPacket(type, transmission, id, fields);
    }

    /// <summary>Serializes the packet, header included, ready for the wire.</summary>
    public byte[] Serialize()
    {
        byte[] body = PlasmaFields.Serialize(Fields);
        int total = HeaderSize + body.Length;

        byte[] packet = new byte[total];
        Encoding.ASCII.GetBytes(Type.PadRight(4)[..4], packet.AsSpan(0, 4));
        uint word = ((uint)Transmission << 24) | (Id & 0x00ff_ffff);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4, 4), word);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8, 4), (uint)total);
        body.CopyTo(packet.AsSpan(HeaderSize));
        return packet;
    }

    /// <summary>Builds a reply to this packet: same type and id, a response transmission, and the given fields.</summary>
    public PlasmaPacket Reply(IDictionary<string, string> fields) =>
        new(Type, PlasmaTransmission.Response, Id, fields);
}

/// <summary>The top byte of the header word. Values are confirmed against a live capture (docs/PLASMA.md).</summary>
public enum PlasmaTransmission : byte
{
    Request = 0xc0,
    Response = 0x80,
    ServerPing = 0xf0,
    ServerMessage = 0xb0,
}
