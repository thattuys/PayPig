using Dalamud.Configuration;

namespace PayPig;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool IsEnabled { get; set; } = false;

    public bool PublicDrain { get; set; } = false;

    // Max gil to send in a single trade. Defaults to 1,000,000.
    public uint MaxGilPerTrade { get; set; } = 1_000_000;

    public void Save()
    {
        // Dalamud writes the config atomically; that replace can intermittently
        // fail on Windows (AV / indexer / file lock). Don't let a transient
        // write error crash the UI — log it and move on.
        try
        {
            Plugin.PluginInterface.SavePluginConfig(this);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to save PayPig configuration.");
        }
    }
}
