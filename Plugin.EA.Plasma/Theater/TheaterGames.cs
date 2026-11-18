using System.Collections.Concurrent;

namespace Plugin.EA.Plasma.Theater;

/// <summary>
/// The games and lobbies Theater serves, shared across all connections (a game
/// one player hosts must be visible to the players who list and join it). One
/// instance per title, held by the plugin.
/// </summary>
public sealed class TheaterGames
{
    private readonly ConcurrentDictionary<long, TheaterGame> _games = new();
    private long _nextGid;
    private long _nextLid = 1;

    /// <summary>
    /// The lobby every game is created under. Plasma titles of this era use a
    /// single server lobby; the id is assigned here, not hard-coded, so more can
    /// be added without touching callers.
    /// </summary>
    public long DefaultLobbyId { get; }

    public TheaterGames()
    {
        DefaultLobbyId = Interlocked.Increment(ref _nextLid);
    }

    public TheaterGame Create(ITheaterPeer host)
    {
        var game = new TheaterGame
        {
            Gid = Interlocked.Increment(ref _nextGid) + 1_000,
            Lid = DefaultLobbyId,
            Host = host,
        };
        _games[game.Gid] = game;
        return game;
    }

    public TheaterGame? Get(long gid) => _games.GetValueOrDefault(gid);

    public IReadOnlyCollection<TheaterGame> InLobby(long lid) =>
        _games.Values.Where(g => g.Lid == lid).ToArray();

    public IReadOnlyCollection<TheaterGame> All => _games.Values.ToArray();

    public void Remove(long gid) => _games.TryRemove(gid, out _);

    /// <summary>Drops every game hosted by a peer that has disconnected.</summary>
    public void RemoveHostedBy(ITheaterPeer peer)
    {
        foreach (TheaterGame game in _games.Values.Where(g => ReferenceEquals(g.Host, peer)).ToArray())
            _games.TryRemove(game.Gid, out _);
    }
}
