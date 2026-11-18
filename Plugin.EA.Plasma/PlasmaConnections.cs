using System.Collections.Concurrent;
using System.Net;
using Plugin.EA.DirtySdk;
using Plugin.EA.DirtySdk.ProtoSsl;
using Plugin.EA.Plasma.Fesl;
using Plugin.EA.Plasma.Packet;
using Plugin.EA.Plasma.Theater;
using ServerShared.Server;

namespace Plugin.EA.Plasma;

/// <summary>
/// Routes the bytes of each TCP connection to the right Plasma service. FESL
/// ports run the ProtoSSL handshake first and then work in plaintext; Theater
/// and Messenger ports are plain text. One <see cref="Connection"/> is kept per
/// session id. Theater games are shared per title, so a game one connection
/// hosts is visible to the connections that list and join it.
/// </summary>
public sealed class PlasmaConnections(string publicHost)
{
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    private readonly ConcurrentDictionary<int, Binding> _ports = new();
    private readonly ConcurrentDictionary<string, TheaterGames> _gamesByTitle = new();

    /// <summary>What each listening port serves.</summary>
    public readonly record struct Binding(string Component, EaPlasmaTitle Title);

    public void Bind(int port, Binding binding)
    {
        _ports[port] = binding;
        _gamesByTitle.GetOrAdd(binding.Title.Descriptor.TitleId, _ => new TheaterGames());
    }

    public void Receive(CoreUnsecureSession session, int port, ReadOnlySpan<byte> data)
    {
        if (!_ports.TryGetValue(port, out Binding binding))
            return;

        Connection connection = _connections.GetOrAdd(session.Id, _ =>
            Connection.Create(binding, publicHost, _gamesByTitle[binding.Title.Descriptor.TitleId], session));

        foreach (byte[] reply in connection.Receive(data))
            session.SendAsync(reply);
    }

    public void Drop(Guid sessionId)
    {
        if (_connections.TryRemove(sessionId, out Connection? connection))
            connection.OnDisconnected();
    }

    /// <summary>Per-connection state: the framer, the service component, and (for FESL) the ProtoSSL layer.</summary>
    private sealed class Connection
    {
        private readonly string _publicHost;
        private readonly PlasmaFramer _framer = new();
        private readonly ProtoSslConnection? _tls;
        private readonly FeslComponent? _fesl;
        private readonly TheaterComponent? _theater;

        private Connection(Binding binding, string publicHost, TheaterGames games, CoreUnsecureSession session)
        {
            _publicHost = publicHost;
            switch (binding.Component)
            {
                case PlasmaComponents.Fesl:
                    _tls = new ProtoSslConnection(DirtySdkRuntime.CertificateFor!($"{binding.Title.Sku}.fesl.ea.com"));
                    _fesl = new FeslComponent(binding.Title);
                    break;
                case PlasmaComponents.Theater:
                    _theater = new TheaterComponent(binding.Title, games, publicHost)
                    {
                        RemoteAddress = (session.Socket?.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "",
                    };
                    // Server-initiated packets (EGRQ to a host, EGEG to a joiner) go straight to the socket.
                    _theater.BindSender(packet => session.SendAsync(packet.Serialize()));
                    break;
                // Messenger is routed here too once implemented.
            }
        }

        public static Connection Create(Binding binding, string publicHost, TheaterGames games, CoreUnsecureSession session) =>
            new(binding, publicHost, games, session);

        public IEnumerable<byte[]> Receive(ReadOnlySpan<byte> data) =>
            _tls is not null ? ReceiveSecure(data) : ReceivePlain(data);

        public void OnDisconnected() => _theater?.OnDisconnected();

        private IEnumerable<byte[]> ReceiveSecure(ReadOnlySpan<byte> data)
        {
            var outbound = new List<byte[]>();
            _tls!.OfferInbound(data);

            byte[] handshake = _tls.DrainOutbound();
            if (handshake.Length > 0)
                outbound.Add(handshake);

            if (!_tls.HandshakeComplete)
                return outbound;

            byte[] plaintext = _tls.ReadApplicationData();
            foreach (PlasmaPacket request in _framer.Receive(plaintext))
                foreach (PlasmaPacket reply in _fesl!.Handle(request, _publicHost))
                {
                    _tls.WriteApplicationData(reply.Serialize());
                    byte[] sealed_ = _tls.DrainOutbound();
                    if (sealed_.Length > 0)
                        outbound.Add(sealed_);
                }
            return outbound;
        }

        private IEnumerable<byte[]> ReceivePlain(ReadOnlySpan<byte> data)
        {
            var outbound = new List<byte[]>();
            foreach (PlasmaPacket request in _framer.Receive(data))
                foreach (PlasmaPacket reply in _theater!.Handle(request))
                    outbound.Add(reply.Serialize());
            return outbound;
        }
    }
}
