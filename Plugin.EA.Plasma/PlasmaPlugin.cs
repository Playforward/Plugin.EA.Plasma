using Plugin.EA.DirtySdk;
using ServerShared.Controllers;
using ServerShared.EventArguments;
using ServerShared.Plugins;
using ServerShared.Server;
using ServerShared.Titles;

namespace Plugin.EA.Plasma;

/// <summary>
/// EA's Plasma-era online services — FESL (login, accounts, stats, presence, over
/// ProtoSSL) with Theater (lobbies and games) and Messenger — served on ServerApp
/// listeners. Which games are served comes from title data (<see cref="EaPlasmaTitle"/>);
/// the ProtoSSL layer and the shared account store come from the EA base plugin
/// through <see cref="DirtySdkRuntime"/>.
/// </summary>
public sealed class PlasmaPlugin : ServerPlugin<PlasmaSettings>
{
    public override uint Priority => 10;
    public override string Name => "EaPlasma";

    private PlasmaConnections? _connections;

    public override void Start()
    {
        if (!DirtySdkRuntime.IsReady)
        {
            Log.Error("[ea/plasma] the EA base plugin (EaDirtySdk) is not loaded; without it there is no ProtoSSL layer. Not starting");
            return;
        }

        var problems = new List<string>();
        List<EaPlasmaTitle> titles = EaPlasmaTitle.LoadAll(TitleRegistry.All, problems);
        foreach (string problem in problems)
            Log.Error("[ea/plasma] {Problem}", problem);
        if (titles.Count == 0)
        {
            Log.Warning("[ea/plasma] no title with ProtocolFamily \"{Family}\"; not starting", EaPlasmaTitle.ProtocolFamily);
            return;
        }

        string host = Config.PublicAddress().ToString();
        _connections = new PlasmaConnections(host);
        BindListeners(titles);

        CoreUnsecureSession.OnBytesReceived += OnBytes;
        CoreUnsecureSession.OnDisconnectedEvent += OnDisconnected;

        Log.Information("[ea/plasma] serving {Titles}; listeners: {Listeners}",
            string.Join(", ", titles), string.Join(", ", Config.Listeners.Select(l => $"{l.Component} {l.Port}")));
    }

    public override void Stop()
    {
        CoreUnsecureSession.OnBytesReceived -= OnBytes;
        CoreUnsecureSession.OnDisconnectedEvent -= OnDisconnected;
        _connections = null;
    }

    private void BindListeners(List<EaPlasmaTitle> titles)
    {
        foreach (PlasmaListener listener in Config.Listeners)
        {
            bool present = ServerController.Servers.Any(s => s.Port == listener.Port && !s.IsUdp);
            if (!present)
            {
                Log.Error("[ea/plasma] no TCP server on port {Port} for {Component}; add it to ServerAppSettings.json", listener.Port, listener.Component);
                continue;
            }

            // A title owns a port through its own port fields; for FESL that is the title's FeslPort.
            EaPlasmaTitle? title = listener.Component == PlasmaComponents.Fesl
                ? titles.FirstOrDefault(t => t.FeslPort == listener.Port)
                : titles.FirstOrDefault(t => t.TheaterPort == listener.Port || t.MessengerPort == listener.Port);

            if (title is null)
            {
                Log.Warning("[ea/plasma] listener {Component} {Port} matches no title's port; left unserved", listener.Component, listener.Port);
                continue;
            }

            _connections!.Bind(listener.Port, new PlasmaConnections.Binding(listener.Component, title));
        }
    }

    private void OnBytes(object? sender, SessionBytesReceivedEventArgs e)
    {
        if (sender is not CoreUnsecureSession session || session.Server is not CoreUnsecureServer server)
            return;
        _connections?.Receive(session, server.Port, e.Data.Span);
    }

    private void OnDisconnected(object? sender, SessionGuidEventArgs e) =>
        _connections?.Drop(e.Id);
}
