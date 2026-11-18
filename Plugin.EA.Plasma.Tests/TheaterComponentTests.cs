using Plugin.EA.Plasma.Packet;
using Plugin.EA.Plasma.Theater;
using ServerShared.Titles;

namespace Plugin.EA.Plasma.Tests;

public class TheaterComponentTests
{
    private static EaPlasmaTitle Title() => new()
    {
        Descriptor = new TitleDescriptor { TitleId = "bad-company-2" },
        Sku = "bfbc2-ps3",
    };

    private static PlasmaPacket Cmd(string type, Dictionary<string, string> fields)
    {
        fields["TID"] = fields.GetValueOrDefault("TID", "1");
        return new PlasmaPacket(type, PlasmaTransmission.Request, 1, fields);
    }

    /// <summary>A Theater connection whose server-initiated pushes are captured for assertions.</summary>
    private static (TheaterComponent Peer, List<PlasmaPacket> Pushed) Peer(EaPlasmaTitle title, TheaterGames games, string addr)
    {
        var pushed = new List<PlasmaPacket>();
        var peer = new TheaterComponent(title, games, "server-pub") { RemoteAddress = addr };
        peer.BindSender(pushed.Add);
        return (peer, pushed);
    }

    [Fact]
    public void Created_game_is_listed_with_its_host_address_and_attributes()
    {
        EaPlasmaTitle title = Title();
        var games = new TheaterGames();
        var (host, _) = Peer(title, games, "203.0.113.9");

        PlasmaPacket created = host.Handle(Cmd("CGAM", new()
        {
            ["NAME"] = "Arica Harbor 24/7",
            ["MAX-PLAYERS"] = "24",
            ["PORT"] = "1000",
            ["B-version"] = "RL-1.0",
        })).Single();
        string gid = created["GID"]!;
        Assert.NotNull(gid);

        List<PlasmaPacket> list = [.. host.Handle(Cmd("GLST", new() { ["LID"] = created["LID"]! }))];
        PlasmaPacket header = list[0];
        PlasmaPacket game = list[1];

        Assert.Equal("1", header["NUM-GAMES"]);
        Assert.Equal(gid, game["GID"]);
        Assert.Equal("Arica Harbor 24/7", game["NAME"]);
        Assert.Equal("203.0.113.9", game["ADDR"]);     // host's connection address, not a configured literal
        Assert.Equal("RL-1.0", game["B-version"]);      // host's attribute echoed verbatim
    }

    [Fact]
    public void Join_forwards_egrq_to_host_then_egeg_to_joiner()
    {
        EaPlasmaTitle title = Title();
        var games = new TheaterGames();
        var (host, hostPushed) = Peer(title, games, "203.0.113.9");
        var (joiner, joinerPushed) = Peer(title, games, "198.51.100.4");

        PlasmaPacket created = host.Handle(Cmd("CGAM", new()
            { ["NAME"] = "g", ["MAX-PLAYERS"] = "24", ["PORT"] = "1000", ["SECRET"] = "s3cr3t" })).Single();
        string gid = created["GID"]!;

        // Joiner asks to enter; the host should receive an EGRQ, the joiner an immediate queued EGAM.
        PlasmaPacket egam = joiner.Handle(Cmd("EGAM", new() { ["GID"] = gid, ["NAME"] = "joiner" })).Single();
        Assert.Equal(gid, egam["GID"]);
        PlasmaPacket egrq = Assert.Single(hostPushed);
        Assert.Equal("EGRQ", egrq.Type);
        string pid = egrq["PID"]!;

        // Host admits the joiner; the joiner should get an EGEG pointing at the host.
        host.Handle(Cmd("EGRS", new() { ["GID"] = gid, ["PID"] = pid, ["ALLOWED"] = "1" })).ToList();
        PlasmaPacket egeg = Assert.Single(joinerPushed);
        Assert.Equal("EGEG", egeg.Type);
        Assert.Equal("203.0.113.9", egeg["ADDR"]);
        Assert.Equal("1000", egeg["PORT"]);
        Assert.Equal("s3cr3t", egeg["EKEY"]);
    }

    [Fact]
    public void Host_disconnect_delists_its_game()
    {
        EaPlasmaTitle title = Title();
        var games = new TheaterGames();
        var (host, _) = Peer(title, games, "203.0.113.9");

        host.Handle(Cmd("CGAM", new() { ["NAME"] = "g", ["MAX-PLAYERS"] = "2", ["PORT"] = "1" })).Single();
        Assert.Single(games.All);

        host.OnDisconnected();
        Assert.Empty(games.All);
    }
}
