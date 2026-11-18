using Plugin.EA.Plasma.Packet;

namespace Plugin.EA.Plasma.Tests;

public class PlasmaPacketTests
{
    [Fact]
    public void Round_trips_header_and_fields()
    {
        var packet = new PlasmaPacket("fsys", PlasmaTransmission.Request, 42, new Dictionary<string, string>
        {
            ["TXN"] = "Hello",
            ["clientString"] = "bfbc2-ps3",
        });

        byte[] wire = packet.Serialize();
        PlasmaPacket? read = PlasmaPacket.TryRead(wire, out int consumed);

        Assert.NotNull(read);
        Assert.Equal(wire.Length, consumed);
        Assert.Equal("fsys", read!.Type);
        Assert.Equal(PlasmaTransmission.Request, read.Transmission);
        Assert.Equal(42u, read.Id);
        Assert.Equal("Hello", read.Txn);
        Assert.Equal("bfbc2-ps3", read["clientString"]);
    }

    [Fact]
    public void TryRead_returns_null_until_a_whole_packet_is_present()
    {
        byte[] wire = new PlasmaPacket("acct", PlasmaTransmission.Response, 1,
            new Dictionary<string, string> { ["TXN"] = "NuPS3Login" }).Serialize();

        Assert.Null(PlasmaPacket.TryRead(wire.AsSpan(0, 8), out int consumed));
        Assert.Equal(0, consumed);
        Assert.NotNull(PlasmaPacket.TryRead(wire, out _));
    }

    [Fact]
    public void Framer_splits_a_concatenated_pair_and_keeps_a_partial_tail()
    {
        byte[] a = new PlasmaPacket("CONN", PlasmaTransmission.Request, 1, new Dictionary<string, string> { ["TID"] = "1" }).Serialize();
        byte[] b = new PlasmaPacket("USER", PlasmaTransmission.Request, 2, new Dictionary<string, string> { ["TID"] = "2" }).Serialize();

        var framer = new PlasmaFramer();
        byte[] stream = [.. a, .. b];

        // Deliver everything but the last 3 bytes: only the first packet is complete.
        List<PlasmaPacket> first = [.. framer.Receive(stream.AsSpan(0, stream.Length - 3))];
        Assert.Single(first);
        Assert.Equal("CONN", first[0].Type);

        // The remaining bytes complete the second packet.
        List<PlasmaPacket> second = [.. framer.Receive(stream.AsSpan(stream.Length - 3))];
        Assert.Single(second);
        Assert.Equal("USER", second[0].Type);
    }

    [Fact]
    public void Reply_keeps_type_and_id_and_marks_a_response()
    {
        var request = new PlasmaPacket("fsys", PlasmaTransmission.Request, 7, new Dictionary<string, string> { ["TXN"] = "Hello" });
        PlasmaPacket reply = request.Reply(new Dictionary<string, string> { ["TXN"] = "Hello" });

        Assert.Equal("fsys", reply.Type);
        Assert.Equal(7u, reply.Id);
        Assert.Equal(PlasmaTransmission.Response, reply.Transmission);
    }
}
