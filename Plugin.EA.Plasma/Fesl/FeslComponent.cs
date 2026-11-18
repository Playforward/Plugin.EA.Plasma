using Plugin.EA.DirtySdk;
using Plugin.EA.DirtySdk.Accounts;
using Plugin.EA.Plasma.Packet;

namespace Plugin.EA.Plasma.Fesl;

/// <summary>
/// The FESL ("Plasma") service over ProtoSSL: the opening handshake, PS3 login,
/// personas, entitlements, terms, ping sites, telemetry, stats and presence.
/// Transactions are named by the <c>TXN</c> field; the reply keeps the request's
/// packet type (<c>fsys</c>/<c>acct</c>/<c>rank</c>/<c>pres</c>/…) and id. Field
/// names are grounded in the BC2 EBOOT; values come from the session and title
/// data, never hard-coded per scenario. Tags: [S] EBOOT string, [guess].
/// </summary>
public sealed class FeslComponent(EaPlasmaTitle title)
{
    private readonly EaPlasmaTitle _title = title;

    /// <summary>The session, once login has created one. Theater reuses it via the login key.</summary>
    public EaSession? Session { get; private set; }

    public IEnumerable<PlasmaPacket> Handle(PlasmaPacket request, string publicHost)
    {
        return request.Txn switch
        {
            "Hello" => [Hello(request, publicHost)],
            "MemCheck" => [MemCheckAck(request)],
            "Ping" => [Ack(request)],
            "GetPingSites" => [GetPingSites(request, publicHost)],
            "NuGetTos" => [GetTos(request)],
            "NuPS3Login" or "NuPS3AddAccount" => [Login(request)],
            "NuGetPersonas" => [GetPersonas(request)],
            "NuLoginPersona" => [LoginPersona(request)],
            "NuGetEntitlements" => [GetEntitlements(request)],
            "GetTelemetryToken" => [GetTelemetryToken(request, publicHost)],
            "PresenceSubscribe" => [Ack(request)],
            "GetStats" or "GetStatsForOwners" => [GetStats(request)],
            "GetAssociations" => [GetAssociations(request)],
            "Goodbye" => Goodbye(request),
            _ => [Ack(request)],
        };
    }

    private PlasmaPacket Hello(PlasmaPacket request, string publicHost)
    {
        string theaterHost = string.IsNullOrEmpty(_title.TheaterHost) ? publicHost : _title.TheaterHost;
        string messengerHost = string.IsNullOrEmpty(_title.MessengerHost) ? publicHost : _title.MessengerHost;

        return request.Reply(new Dictionary<string, string>
        {
            ["TXN"] = "Hello",
            ["domainPartition.domain"] = "ps3",              // [S domainPartition.domain]
            ["domainPartition.subDomain"] = _title.Sku,       // [S domainPartition.subDomain]
            ["curTime"] = DateTimeOffset.UtcNow.ToString("MMM-dd-yyyy HH:mm:ss 'UTC'"),
            ["activityTimeoutSecs"] = "240",                  // [guess]
            ["theaterIp"] = theaterHost,                      // [S theaterIp]
            ["theaterPort"] = _title.TheaterPort.ToString(),  // [S theaterPort]
            ["messengerIp"] = messengerHost,                  // [S messengerIp]
            ["messengerPort"] = _title.MessengerPort.ToString(),
        });
    }

    // The server normally initiates MemCheck as a keep-alive; when the client echoes one we just acknowledge.
    private static PlasmaPacket MemCheckAck(PlasmaPacket request) =>
        request.Reply(new Dictionary<string, string> { ["TXN"] = "MemCheck" });

    private PlasmaPacket GetPingSites(PlasmaPacket request, string publicHost)
    {
        // QoS ping sites. One site (this server) for now; the list is data-driven, not a fixed scenario.
        var fields = new Dictionary<string, string>
        {
            ["TXN"] = "GetPingSites",
            ["minPingSitesToPing"] = "1",                     // [S minPingSitesToPing]
        };
        PlasmaFields.SetList(fields, "pingSite", []);
        fields["pingSite.[]"] = "1";
        fields["pingSite.0.addr"] = publicHost;
        fields["pingSite.0.type"] = "0";                      // [guess] QoS probe type
        fields["pingSite.0.name"] = "eu1";                    // [guess] region label
        return request.Reply(fields);
    }

    private PlasmaPacket GetTos(PlasmaPacket request)
    {
        // Terms of service: served from the title's ea/tos.txt if present, else a minimal placeholder.
        string path = Path.Combine(_title.Directory, "tos.txt");
        string tos = File.Exists(path) ? File.ReadAllText(path) : "Terms of service unavailable.";
        return request.Reply(new Dictionary<string, string>
        {
            ["TXN"] = "NuGetTos",
            ["version"] = "1.0",                              // [guess] confirm the version the client expects
            ["tos"] = tos,
        });
    }

    private PlasmaPacket Login(PlasmaPacket request)
    {
        // A full login verifies the PS3 NP ticket in the request; here we map its identity to an account and
        // open a session so Theater has something to pair with. Ticket verification is the next step.
        string name = request["name"] ?? request["nuid"] ?? request["consoleId"] ?? "player";
        EaAccount account = DirtySdkRuntime.Accounts.GetOrCreate(name, origin: "ps3-ticket");
        if (account.Personas.Count == 0)
            DirtySdkRuntime.Accounts.AddPersona(account, name);   // PS3 online id doubles as the persona
        Session = DirtySdkRuntime.Accounts.OpenSession(account, _title.Descriptor.TitleId);

        return request.Reply(new Dictionary<string, string>
        {
            ["TXN"] = request.Txn,
            ["nuid"] = account.Name,                          // [S nuid]
            ["lkey"] = Session.LoginKey,                      // [S lkey]
            ["profileId"] = account.Id.ToString(),
            ["userId"] = account.Id.ToString(),
        });
    }

    private PlasmaPacket GetPersonas(PlasmaPacket request)
    {
        EaAccount? account = Session?.Account;
        var names = account?.Personas.Select(p => p.Name).ToList() ?? [];
        var fields = new Dictionary<string, string> { ["TXN"] = "NuGetPersonas" };
        PlasmaFields.SetList(fields, "personas", names);
        return request.Reply(fields);
    }

    private PlasmaPacket LoginPersona(PlasmaPacket request)
    {
        string wanted = request["name"] ?? request["personaName"] ?? "";
        EaPersona? persona = Session?.Account.Personas.FirstOrDefault(p =>
            string.Equals(p.Name, wanted, StringComparison.Ordinal)) ?? Session?.Account.Personas.FirstOrDefault();
        if (Session is not null)
            Session.Persona = persona;

        return request.Reply(new Dictionary<string, string>
        {
            ["TXN"] = "NuLoginPersona",
            ["lkey"] = Session?.LoginKey ?? "",
            ["profileId"] = persona?.Id.ToString() ?? "0",
            ["userId"] = Session?.Account.Id.ToString() ?? "0",
            ["personaName"] = persona?.Name ?? "",            // [S personaName]
        });
    }

    private PlasmaPacket GetEntitlements(PlasmaPacket request)
    {
        // Entitlements the game checks (base game, DLC). Tags come from title data, not invented here.
        var fields = new Dictionary<string, string> { ["TXN"] = "NuGetEntitlements" };
        fields["entitlements.[]"] = _title.Entitlements.Count.ToString();
        for (int i = 0; i < _title.Entitlements.Count; i++)
        {
            fields[$"entitlements.{i}.entitlementTag"] = _title.Entitlements[i];   // [S entitlementTag]
            fields[$"entitlements.{i}.userId"] = Session?.Account.Id.ToString() ?? "0";
            fields[$"entitlements.{i}.status"] = "ACTIVE";                          // [guess]
        }
        return request.Reply(fields);
    }

    private PlasmaPacket GetTelemetryToken(PlasmaPacket request, string publicHost) =>
        request.Reply(new Dictionary<string, string>
        {
            ["TXN"] = "GetTelemetryToken",
            ["telemetryToken"] = Session?.LoginKey ?? "",     // opaque; the client just echoes it to telemetry
            ["enabled"] = "US",                               // [guess]
            ["ip"] = publicHost,
        });

    private PlasmaPacket GetStats(PlasmaPacket request)
    {
        // Echo the requested stat keys with zero values until a stats store exists. Keys come from the request.
        var fields = new Dictionary<string, string> { ["TXN"] = request.Txn };
        int count = (int)(ParseLong(request["keys.[]"]) ?? 0);
        fields["stats.[]"] = count.ToString();
        for (int i = 0; i < count; i++)
        {
            string key = request[$"keys.{i}"] ?? "";
            fields[$"stats.{i}.key"] = key;
            fields[$"stats.{i}.value"] = "0.0";                // [guess] no stats stored yet
        }
        return request.Reply(fields);
    }

    private PlasmaPacket GetAssociations(PlasmaPacket request)
    {
        // Friends / mute / recent-players lists; empty until presence is implemented. Type comes from the request.
        var fields = new Dictionary<string, string>
        {
            ["TXN"] = "GetAssociations",
            ["type"] = request["type"] ?? "",                 // [S type]
            ["result.[]"] = "0",
            ["maxListSize"] = "100",                          // [guess]
        };
        return request.Reply(fields);
    }

    private IEnumerable<PlasmaPacket> Goodbye(PlasmaPacket request)
    {
        if (Session is not null)
            DirtySdkRuntime.Accounts.CloseSession(Session.LoginKey);
        Session = null;
        return [request.Reply(new Dictionary<string, string> { ["TXN"] = "Goodbye" })];
    }

    private static PlasmaPacket Ack(PlasmaPacket request) =>
        request.Reply(new Dictionary<string, string> { ["TXN"] = request.Txn });

    private static long? ParseLong(string? value) =>
        long.TryParse(value, out long result) ? result : null;
}
