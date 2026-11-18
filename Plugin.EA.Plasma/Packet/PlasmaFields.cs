using System.Text;

namespace Plugin.EA.Plasma.Packet;

/// <summary>
/// The <c>key=value</c> body codec shared by FESL and Theater. Scalars are one
/// line each; lists and maps are expressed with dotted keys, e.g.
/// <c>keys.[]=2</c>, <c>keys.0=foo</c>, <c>keys.1=bar</c>, and nested records as
/// <c>users.0.name=…</c>. The body is terminated by a trailing NUL. This is our
/// own implementation of the documented format.
/// </summary>
public static class PlasmaFields
{
    public static Dictionary<string, string> Parse(ReadOnlySpan<byte> body)
    {
        // Drop a trailing NUL terminator if present.
        if (!body.IsEmpty && body[^1] == 0)
            body = body[..^1];

        var fields = new Dictionary<string, string>();
        string text = Encoding.UTF8.GetString(body);
        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0)
                continue;
            int eq = line.IndexOf('=');
            if (eq < 0)
            {
                fields[line] = "";
                continue;
            }
            string key = line[..eq];
            string value = line[(eq + 1)..];
            // Values are sometimes wrapped in double quotes; keep the inner text.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            fields[key] = value;
        }
        return fields;
    }

    public static byte[] Serialize(IReadOnlyDictionary<string, string> fields)
    {
        var builder = new StringBuilder();
        foreach ((string key, string value) in fields)
            builder.Append(key).Append('=').Append(value).Append('\n');
        builder.Append('\0');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>Writes a list under <paramref name="key"/> as <c>key.[]=count</c> plus <c>key.i=value</c> lines.</summary>
    public static void SetList(IDictionary<string, string> fields, string key, IReadOnlyList<string> values)
    {
        fields[$"{key}.[]"] = values.Count.ToString();
        for (int i = 0; i < values.Count; i++)
            fields[$"{key}.{i}"] = values[i];
    }
}
