using Plugin.EA.Plasma.Fesl;
using Plugin.EA.Plasma.Packet;
using ServerShared.Titles;

namespace Plugin.EA.Plasma.Tests;

public class FeslComponentTests
{
    private static EaPlasmaTitle Title() => new()
    {
        Descriptor = new TitleDescriptor { TitleId = "bad-company-2" },
        Sku = "bfbc2-ps3",
        TheaterPort = 18126,
        MessengerPort = 0,
        Entitlements = ["ONLINE_ACCESS", "VIETNAM"],
    };

    private static PlasmaPacket Req(string type, string txn, Dictionary<string, string>? extra = null)
    {
        var fields = new Dictionary<string, string> { ["TXN"] = txn };
        if (extra is not null)
            foreach ((string k, string v) in extra) fields[k] = v;
        return new PlasmaPacket(type, PlasmaTransmission.Request, 1, fields);
    }

    [Fact]
    public void Hello_returns_theater_address_from_title()
    {
        var fesl = new FeslComponent(Title());
        PlasmaPacket reply = fesl.Handle(Req("fsys", "Hello"), "10.0.0.5").Single();

        Assert.Equal("Hello", reply.Txn);
        Assert.Equal("10.0.0.5", reply["theaterIp"]);
        Assert.Equal("18126", reply["theaterPort"]);
        Assert.Equal("bfbc2-ps3", reply["domainPartition.subDomain"]);
    }

    [Fact]
    public void Login_opens_a_session_and_returns_a_login_key()
    {
        var fesl = new FeslComponent(Title());
        PlasmaPacket reply = fesl.Handle(Req("acct", "NuPS3Login", new() { ["name"] = "tester" }), "h").Single();

        Assert.NotNull(fesl.Session);
        Assert.Equal(fesl.Session!.LoginKey, reply["lkey"]);
        Assert.Equal("tester", reply["nuid"]);
    }

    [Fact]
    public void Login_then_personas_lists_the_online_id_persona()
    {
        var fesl = new FeslComponent(Title());
        fesl.Handle(Req("acct", "NuPS3Login", new() { ["name"] = "tester" }), "h").Single();
        PlasmaPacket reply = fesl.Handle(Req("acct", "NuGetPersonas"), "h").Single();

        Assert.Equal("1", reply["personas.[]"]);
        Assert.Equal("tester", reply["personas.0"]);
    }

    [Fact]
    public void Entitlements_come_from_title_data()
    {
        var fesl = new FeslComponent(Title());
        fesl.Handle(Req("acct", "NuPS3Login", new() { ["name"] = "t" }), "h").Single();
        PlasmaPacket reply = fesl.Handle(Req("acct", "NuGetEntitlements"), "h").Single();

        Assert.Equal("2", reply["entitlements.[]"]);
        Assert.Equal("ONLINE_ACCESS", reply["entitlements.0.entitlementTag"]);
        Assert.Equal("VIETNAM", reply["entitlements.1.entitlementTag"]);
    }

    [Fact]
    public void GetPingSites_advertises_the_server_as_a_site()
    {
        var fesl = new FeslComponent(Title());
        PlasmaPacket reply = fesl.Handle(Req("fsys", "GetPingSites"), "10.0.0.5").Single();

        Assert.Equal("1", reply["pingSite.[]"]);
        Assert.Equal("10.0.0.5", reply["pingSite.0.addr"]);
    }
}
