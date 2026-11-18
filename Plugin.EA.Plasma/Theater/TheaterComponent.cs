using Plugin.EA.DirtySdk;
using Plugin.EA.Plasma.Packet;

namespace Plugin.EA.Plasma.Theater;

/// <summary>
/// The Theater service: game lobbies, hosted games, listing and joining. Plain
/// text (no ProtoSSL). Commands are the four-character packet type. One instance
/// per connection; it is also the <see cref="ITheaterPeer"/> the game registry
/// pushes server-initiated packets to (a host's <c>EGRQ</c>, a joiner's <c>EGEG</c>).
/// Field names are grounded in the BC2 EBOOT; values the host sets are echoed
/// verbatim, so nothing is assumed per title. Tags: [S] EBOOT string, [guess].
/// </summary>
public sealed class TheaterComponent(EaPlasmaTitle title, TheaterGames games, string publicHost) : ITheaterPeer
{
    private readonly EaPlasmaTitle _title = title;
    private readonly TheaterGames _games = games;
    private readonly string _publicHost = publicHost;

    private Action<PlasmaPacket>? _send;
    private string _loginKey = "";

    /// <summary>The peer's reachable address, taken from its TCP connection by the plugin.</summary>
    public string RemoteAddress { get; set; } = "";

    public string LoginKey => _loginKey;

    /// <summary>Set by the plugin so the registry can push packets to this connection.</summary>
    public void BindSender(Action<PlasmaPacket> send) => _send = send;

    public void Send(PlasmaPacket packet) => _send?.Invoke(packet);

    /// <summary>Call when the connection drops, so its hosted games are delisted.</summary>
    public void OnDisconnected() => _games.RemoveHostedBy(this);

    public IEnumerable<PlasmaPacket> Handle(PlasmaPacket request)
    {
        return request.Type switch
        {
            "CONN" => [Conn(request)],
            "USER" => [User(request)],
            "LLST" => ListLobbies(request),
            "GLST" => ListGames(request),
            "GDAT" => [GameData(request)],
            "GDET" => [GameDetails(request)],
            "CGAM" => [CreateGame(request)],
            "EGAM" => EnterGame(request),
            "EGRS" => EnterGameResponse(request),
            "ECNL" => [CancelEnter(request)],
            "PENT" => [Acknowledge(request)],
            "PLVT" => [LeaveGame(request)],
            "UBRA" => [Acknowledge(request)],
            "UGAM" => [UpdateGame(request)],
            "PING" => [new PlasmaPacket("PING", PlasmaTransmission.Response, request.Id, new Dictionary<string, string>())],
            _ => [Acknowledge(request)],
        };
    }

    private PlasmaPacket Conn(PlasmaPacket request) => new("CONN", PlasmaTransmission.Response, request.Id,
        new Dictionary<string, string>
        {
            ["TID"] = request["TID"] ?? "0",
            ["PROT"] = request["PROT"] ?? "2",         // [S PROT] protocol version the client offered
            ["TIME"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["activityTimeoutSecs"] = "240",            // [guess] keep-alive window; confirm live
        });

    private PlasmaPacket User(PlasmaPacket request)
    {
        _loginKey = request["LKEY"] ?? "";              // [S LKEY] the key FESL issued at login
        if (DirtySdkRuntime.Accounts.FindSession(_loginKey) is null)
            Log.Warning("[ea/theater] {Title}: USER presented an unknown login key", _title);
        return new PlasmaPacket("USER", PlasmaTransmission.Response, request.Id,
            new Dictionary<string, string> { ["TID"] = request["TID"] ?? "0", ["NAME"] = request["NAME"] ?? "" });
    }

    private IEnumerable<PlasmaPacket> ListLobbies(PlasmaPacket request)
    {
        // LLST header then one LDAT per lobby. Plasma titles here use a single lobby.
        yield return new PlasmaPacket("LLST", PlasmaTransmission.Response, request.Id, new Dictionary<string, string>
        {
            ["TID"] = request["TID"] ?? "0",
            ["NUM-LOBBIES"] = "1",
        });
        yield return new PlasmaPacket("LDAT", PlasmaTransmission.Response, request.Id, new Dictionary<string, string>
        {
            ["TID"] = request["TID"] ?? "0",
            ["LID"] = _games.DefaultLobbyId.ToString(),
            ["NAME"] = _title.Sku,                      // lobby named after the SKU; not a scenario literal
            ["LOCALE"] = "en_US",                        // [guess]
            ["NUM-GAMES"] = _games.InLobby(_games.DefaultLobbyId).Count.ToString(),
        });
    }

    private IEnumerable<PlasmaPacket> ListGames(PlasmaPacket request)
    {
        long lid = ParseLong(request["LID"]) ?? _games.DefaultLobbyId;
        IReadOnlyCollection<TheaterGame> list = _games.InLobby(lid);

        yield return new PlasmaPacket("GLST", PlasmaTransmission.Response, request.Id, new Dictionary<string, string>
        {
            ["TID"] = request["TID"] ?? "0",
            ["LID"] = lid.ToString(),
            ["LOBBY-NUM-GAMES"] = list.Count.ToString(),
            ["NUM-GAMES"] = list.Count.ToString(),
        });
        foreach (TheaterGame game in list)
            yield return GameSummary("GDAT", request["TID"] ?? "0", request.Id, game);
    }

    private PlasmaPacket GameData(PlasmaPacket request)
    {
        TheaterGame? game = _games.Get(ParseLong(request["GID"]) ?? -1);
        return game is null
            ? Error(request, "GDAT", 1)
            : GameSummary("GDAT", request["TID"] ?? "0", request.Id, game);
    }

    private PlasmaPacket GameDetails(PlasmaPacket request)
    {
        TheaterGame? game = _games.Get(ParseLong(request["GID"]) ?? -1);
        if (game is null)
            return Error(request, "GDET", 1);

        var fields = new Dictionary<string, string>
        {
            ["TID"] = request["TID"] ?? "0",
            ["LID"] = game.Lid.ToString(),
            ["GID"] = game.Gid.ToString(),
            ["D-TotalPlayers"] = game.PlayerCount.ToString(),
        };
        // Per-player details: pid/name indexed as the client expects.
        int i = 0;
        foreach (TheaterMember member in game.Members.Values)
        {
            fields[$"PID.{i}"] = member.Pid.ToString();
            fields[$"NAME.{i}"] = member.Name;
            i++;
        }
        return new PlasmaPacket("GDET", PlasmaTransmission.Response, request.Id, fields);
    }

    private PlasmaPacket CreateGame(PlasmaPacket request)
    {
        TheaterGame game = _games.Create(this);
        game.Name = request["NAME"] ?? _title.Sku;
        game.MaxPlayers = (int)(ParseLong(request["MAX-PLAYERS"]) ?? 0);
        game.HostAddress = string.IsNullOrEmpty(RemoteAddress) ? _publicHost : RemoteAddress;
        game.HostPort = (int)(ParseLong(request["PORT"]) ?? 0);
        game.InternalAddress = request["INT-IP"] ?? "";          // [S INT-IP]
        game.InternalPort = (int)(ParseLong(request["INT-PORT"]) ?? 0);
        game.Secret = request["SECRET"] ?? "";                   // [S SECRET]
        foreach ((string key, string value) in request.Fields)   // keep the host's B-* attributes verbatim [S B-*]
            if (key.StartsWith("B-", StringComparison.Ordinal))
                game.Attributes[key] = value;

        // The host occupies the first player slot.
        int pid = game.NextPid();
        game.Members[pid] = new TheaterMember(pid, this, request["NAME"] ?? "");

        return new PlasmaPacket("CGAM", PlasmaTransmission.Response, request.Id, new Dictionary<string, string>
        {
            ["TID"] = request["TID"] ?? "0",
            ["GID"] = game.Gid.ToString(),
            ["LID"] = game.Lid.ToString(),
            ["MAX-PLAYERS"] = game.MaxPlayers.ToString(),
            ["EKEY"] = game.Secret,                              // [S EKEY] echoed so the host can match joiners
            ["HU"] = pid.ToString(),                             // host's player id in the game
        });
    }

    private IEnumerable<PlasmaPacket> EnterGame(PlasmaPacket request)
    {
        TheaterGame? game = _games.Get(ParseLong(request["GID"]) ?? -1);
        if (game is null)
        {
            yield return Error(request, "EGAM", 1);
            yield break;
        }

        int pid = game.NextPid();
        game.Members[pid] = new TheaterMember(pid, this, request["NAME"] ?? "");

        // Ask the host to admit the joiner (EGRQ); its EGRS comes back on the host's connection.
        game.Host.Send(new PlasmaPacket("EGRQ", PlasmaTransmission.ServerMessage, 0, new Dictionary<string, string>
        {
            ["GID"] = game.Gid.ToString(),
            ["PID"] = pid.ToString(),
            ["NAME"] = request["NAME"] ?? "",
            ["R-INT-IP"] = request["R-INT-IP"] ?? RemoteAddress,
            ["R-INT-PORT"] = request["R-INT-PORT"] ?? "0",
            ["INT-IP"] = request["INT-IP"] ?? "",
            ["INT-PORT"] = request["INT-PORT"] ?? "0",
        }));

        // Tell the joiner it is queued; the go-ahead (EGEG) follows when the host answers.
        yield return new PlasmaPacket("EGAM", PlasmaTransmission.Response, request.Id, new Dictionary<string, string>
        {
            ["TID"] = request["TID"] ?? "0",
            ["LID"] = game.Lid.ToString(),
            ["GID"] = game.Gid.ToString(),
            ["PID"] = pid.ToString(),
        });
    }

    private IEnumerable<PlasmaPacket> EnterGameResponse(PlasmaPacket request)
    {
        // Sent by the host in answer to EGRQ. Route the go-ahead to the waiting joiner.
        TheaterGame? game = _games.Get(ParseLong(request["GID"]) ?? -1);
        int pid = (int)(ParseLong(request["PID"]) ?? -1);
        if (game is null || !game.Members.TryGetValue(pid, out TheaterMember? member))
            yield break;

        bool allowed = (request["ALLOWED"] ?? "1") == "1";
        if (!allowed)
        {
            game.Members.Remove(pid);
            member.Peer.Send(new PlasmaPacket("EGEG", PlasmaTransmission.ServerMessage, 0, new Dictionary<string, string>
            {
                ["GID"] = game.Gid.ToString(),
                ["PID"] = pid.ToString(),
                ["ALLOWED"] = "0",
            }));
            yield break;
        }

        member.Peer.Send(new PlasmaPacket("EGEG", PlasmaTransmission.ServerMessage, 0, new Dictionary<string, string>
        {
            ["GID"] = game.Gid.ToString(),
            ["LID"] = game.Lid.ToString(),
            ["PID"] = pid.ToString(),
            ["ADDR"] = game.HostAddress,                        // host's address for the P2P connection
            ["PORT"] = game.HostPort.ToString(),
            ["INT-IP"] = game.InternalAddress,
            ["INT-PORT"] = game.InternalPort.ToString(),
            ["EKEY"] = game.Secret,
        }));
        // Acknowledge the host's EGRS.
        yield return Acknowledge(request);
    }

    private PlasmaPacket UpdateGame(PlasmaPacket request)
    {
        TheaterGame? game = _games.Get(ParseLong(request["GID"]) ?? -1);
        if (game is not null)
            foreach ((string key, string value) in request.Fields)
                if (key.StartsWith("B-", StringComparison.Ordinal))
                    game.Attributes[key] = value;
        return Acknowledge(request);
    }

    private PlasmaPacket LeaveGame(PlasmaPacket request)
    {
        TheaterGame? game = _games.Get(ParseLong(request["GID"]) ?? -1);
        int pid = (int)(ParseLong(request["PID"]) ?? -1);
        if (game is not null)
        {
            game.Members.Remove(pid);
            if (ReferenceEquals(game.Host, this))
                _games.Remove(game.Gid);                        // host left: close the game
        }
        return Acknowledge(request);
    }

    private PlasmaPacket CancelEnter(PlasmaPacket request)
    {
        TheaterGame? game = _games.Get(ParseLong(request["GID"]) ?? -1);
        int pid = (int)(ParseLong(request["PID"]) ?? -1);
        game?.Members.Remove(pid);
        return Acknowledge(request);
    }

    private static PlasmaPacket GameSummary(string type, string tid, uint id, TheaterGame game)
    {
        var fields = new Dictionary<string, string>
        {
            ["TID"] = tid,
            ["LID"] = game.Lid.ToString(),
            ["GID"] = game.Gid.ToString(),
            ["NAME"] = game.Name,
            ["HN"] = game.HostAddress,                          // host name/address
            ["HU"] = "1",
            ["MAX-PLAYERS"] = game.MaxPlayers.ToString(),
            ["F"] = game.PlayerCount.ToString(),                // filled slots [guess]
            ["ADDR"] = game.HostAddress,
            ["PORT"] = game.HostPort.ToString(),
        };
        foreach ((string key, string value) in game.Attributes)
            fields[key] = value;
        return new PlasmaPacket(type, PlasmaTransmission.Response, id, fields);
    }

    private static PlasmaPacket Acknowledge(PlasmaPacket request) =>
        new(request.Type, PlasmaTransmission.Response, request.Id,
            new Dictionary<string, string> { ["TID"] = request["TID"] ?? "0" });

    private static PlasmaPacket Error(PlasmaPacket request, string type, int code) =>
        new(type, PlasmaTransmission.Response, request.Id,
            new Dictionary<string, string> { ["TID"] = request["TID"] ?? "0", ["errorCode"] = code.ToString() });

    private static long? ParseLong(string? value) =>
        long.TryParse(value, out long result) ? result : null;
}
