using System.Text.Json;

namespace PayPig;

/// <summary>
/// Loads/saves the trade whitelist (whitelist.json) and answers lookups and
/// daily-limit questions.
/// </summary>
public sealed class Whitelist
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string path;
    private List<WhitelistEntry> entries = new();

    public Whitelist(string path)
    {
        this.path = path;
        Load();
    }

    public IReadOnlyList<WhitelistEntry> Entries => entries;

    public void Load()
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                entries = JsonSerializer.Deserialize<List<WhitelistEntry>>(json, JsonOpts) ?? new();
            }
            else
            {
                entries = new();
                Save(); // create an empty file so it's easy to find and edit
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to load whitelist from {Path}", path);
            entries = new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOpts));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to save whitelist to {Path}", path);
        }
    }

    public void Add(WhitelistEntry entry)
    {
        entries.Add(entry);
        Save();
    }

    public bool Remove(WhitelistEntry entry)
    {
        var removed = entries.Remove(entry);
        if (removed)
            Save();
        return removed;
    }

    /// <summary>Find a whitelisted partner by name (case-insensitive) + world. Null if not listed.</summary>
    public WhitelistEntry? Find(string name, uint worldId)
        => entries.FirstOrDefault(e =>
            e.WorldId == worldId &&
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gil still allowed to send to this person today. Returns null when there
    /// is no daily limit (limit == 0). Resets automatically when the day rolls.
    /// </summary>
    public uint? RemainingToday(WhitelistEntry entry, string today)
    {
        if (entry.Limit == 0)
            return null; // no daily limit

        var sentToday = entry.Date == today ? entry.Sent : 0u;
        return sentToday >= entry.Limit ? 0u : entry.Limit - sentToday;
    }

    /// <summary>
    /// Record gil that actually went through, counting it against today's
    /// limit. Call this on trade COMPLETION, not when the window opens.
    /// </summary>
    public void RecordSent(WhitelistEntry entry, uint amount, string today)
    {
        if (entry.Date != today)
        {
            entry.Date = today;
            entry.Sent = 0;
        }

        entry.Sent += amount;
        Save();
    }
}
