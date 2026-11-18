using System.Text.Json;
using System.Text.Json.Serialization;
using ServerShared.Titles;

namespace Plugin.EA.Plasma;

/// <summary>
/// One Plasma-era EA game, read from <c>&lt;ResourceDir&gt;/ea/title.json</c> of a
/// title whose <c>ProtocolFamily</c> is <c>ea-plasma</c>. Everything that varies
/// between games (the SKU/host prefix, the ports, the entitlement tags) is data
/// here, so the code stays game-agnostic.
/// </summary>
public sealed class EaPlasmaTitle
{
    public const string ProtocolFamily = "ea-plasma";
    public const string FileName = "ea/title.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [JsonIgnore]
    public TitleDescriptor Descriptor { get; set; } = new();

    [JsonIgnore]
    public string Directory => Path.Combine(Descriptor.ResourceDir, "ea");

    /// <summary>
    /// The game's FESL SKU / host prefix, e.g. <c>bfbc2-ps3</c> for
    /// <c>bfbc2-ps3.fesl.ea.com</c>. The client connects to this host (redirected
    /// to our server) and we answer for it.
    /// </summary>
    public string Sku { get; set; } = "";

    /// <summary>The <c>clientString</c> the game sends at FESL Hello, used to tell titles apart on a shared port.</summary>
    public string ClientString { get; set; } = "";

    /// <summary>FESL listener port for this title (the SSLv3 service).</summary>
    public int FeslPort { get; set; }

    /// <summary>Theater host and port announced in the FESL Hello reply. Empty host = the server's public address.</summary>
    public string TheaterHost { get; set; } = "";
    public int TheaterPort { get; set; }

    /// <summary>Messenger host and port announced in the FESL Hello reply.</summary>
    public string MessengerHost { get; set; } = "";
    public int MessengerPort { get; set; }

    /// <summary>Entitlement tags the game checks at login (base game, DLC). Extracted from the title, not invented here.</summary>
    public List<string> Entitlements { get; set; } = [];

    public override string ToString() => Descriptor.ToString();

    public static List<EaPlasmaTitle> LoadAll(IEnumerable<TitleDescriptor> titles, List<string> problems)
    {
        var loaded = new List<EaPlasmaTitle>();
        foreach (TitleDescriptor descriptor in titles.Where(t =>
                     string.Equals(t.ProtocolFamily, ProtocolFamily, StringComparison.OrdinalIgnoreCase)))
        {
            string path = Path.Combine(descriptor.ResourceDir, FileName);
            if (!File.Exists(path))
            {
                problems.Add($"{descriptor}: {FileName} not found at {path}");
                continue;
            }
            try
            {
                EaPlasmaTitle title = JsonSerializer.Deserialize<EaPlasmaTitle>(File.ReadAllText(path), JsonOptions)
                                      ?? throw new InvalidDataException("empty title file");
                title.Descriptor = descriptor;
                if (string.IsNullOrWhiteSpace(title.Sku))
                    problems.Add($"{descriptor}: ea/title.json has no Sku");
                loaded.Add(title);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                problems.Add($"{descriptor}: {FileName} could not be read: {exception.Message}");
            }
        }
        return loaded;
    }
}
