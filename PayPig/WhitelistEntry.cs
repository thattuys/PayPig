using System.Text.Json.Serialization;

namespace PayPig;

/// <summary>
/// One whitelisted trade partner. Matched by <see cref="Name"/> +
/// <see cref="WorldId"/>. Serialized to whitelist.json.
/// </summary>
public class WhitelistEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("worldid")]
    public uint WorldId { get; set; }

    /// <summary>Max gil that may be sent to this person per day. 0 = no daily limit.</summary>
    [JsonPropertyName("limit")]
    public uint Limit { get; set; }

    /// <summary>The day (yyyy-MM-dd) that <see cref="Sent"/> applies to.</summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>Gil already sent to this person on <see cref="Date"/>.</summary>
    [JsonPropertyName("sent")]
    public uint Sent { get; set; }
}
