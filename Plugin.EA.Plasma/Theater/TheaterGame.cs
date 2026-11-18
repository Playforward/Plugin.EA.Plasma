using Plugin.EA.Plasma.Packet;

namespace Plugin.EA.Plasma.Theater;

/// <summary>
/// One hosted game registered through Theater (<c>CGAM</c>). Players find it with
/// <c>GLST</c>/<c>GDAT</c> and join it with <c>EGAM</c>. The host keeps its Theater
/// connection open, so the server can forward a join request to it (<c>EGRQ</c>)
/// and wait for its answer (<c>EGRS</c>).
/// </summary>
public sealed class TheaterGame
{
    public required long Gid { get; init; }
    public required long Lid { get; init; }

    /// <summary>The Theater connection that is hosting, so the server can send it <c>EGRQ</c>.</summary>
    public required ITheaterPeer Host { get; init; }

    public string Name { get; set; } = "";
    public int MaxPlayers { get; set; }

    /// <summary>The host's reachable address/port for peers, as the host reported them at <c>CGAM</c>.</summary>
    public string HostAddress { get; set; } = "";
    public int HostPort { get; set; }
    public string InternalAddress { get; set; } = "";
    public int InternalPort { get; set; }

    /// <summary>The join secret the host hands out; echoed to joiners so the host can match them.</summary>
    public string Secret { get; set; } = "";

    /// <summary>
    /// Game attributes the host set at create time (Theater's <c>B-*</c> keys, e.g.
    /// <c>B-version</c>, <c>B-maxGameSize</c>, <c>B-U-*</c> user attributes). Kept
    /// verbatim so <c>GDAT</c>/<c>GDET</c> report exactly what the host set — no
    /// per-title assumptions baked in.
    /// </summary>
    public Dictionary<string, string> Attributes { get; } = [];

    /// <summary>Players currently in or joining the game, by their Theater player id.</summary>
    public Dictionary<int, TheaterMember> Members { get; } = [];

    public int PlayerCount => Members.Count;

    private int _nextPid = 1;
    public int NextPid() => _nextPid++;
}

public sealed record TheaterMember(int Pid, ITheaterPeer Peer, string Name);

/// <summary>
/// A Theater connection the server can push packets to (the host, or a joiner
/// awaiting its enter-game answer). Lets the game registry reach a peer other
/// than the one whose request is being handled.
/// </summary>
public interface ITheaterPeer
{
    /// <summary>The logged-in session's login key, or empty before <c>USER</c>.</summary>
    string LoginKey { get; }

    /// <summary>Pushes a server-initiated packet to this peer's socket.</summary>
    void Send(PlasmaPacket packet);
}
