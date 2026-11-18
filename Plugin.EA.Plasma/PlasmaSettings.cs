using System.Net;
using System.Net.Sockets;

namespace Plugin.EA.Plasma;

/// <summary>Plugin-wide settings, kept in <c>Configs/EaPlasmaConfig.json</c>. Per-game ports come from title data.</summary>
public sealed class PlasmaSettings
{
    /// <summary>Address announced to clients as Theater/Messenger. Empty = this machine's first non-loopback IPv4.</summary>
    public string PublicHost { get; set; } = "";

    /// <summary>
    /// Which service answers on which ServerApp listener. Each port also needs an
    /// entry in ServerAppSettings.json. FESL ports are ProtoSSL (SSLv3); Theater
    /// and Messenger are plain text.
    /// </summary>
    public List<PlasmaListener> Listeners { get; set; } = [];

    public bool LogTraffic { get; set; } = true;

    public IPAddress PublicAddress()
    {
        if (!string.IsNullOrWhiteSpace(PublicHost))
        {
            if (IPAddress.TryParse(PublicHost, out IPAddress? parsed))
                return parsed;
            return Dns.GetHostAddresses(PublicHost).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        return Dns.GetHostAddresses(Dns.GetHostName())
                  .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
               ?? IPAddress.Loopback;
    }
}

public sealed class PlasmaListener
{
    public string Component { get; set; } = "";
    public int Port { get; set; }
}

public static class PlasmaComponents
{
    /// <summary>FESL/Plasma over ProtoSSL (SSLv3).</summary>
    public const string Fesl = "FESL";

    /// <summary>Theater, plain text.</summary>
    public const string Theater = "THEATER";

    /// <summary>Messenger (presence/IM), plain text.</summary>
    public const string Messenger = "MESSENGER";
}
