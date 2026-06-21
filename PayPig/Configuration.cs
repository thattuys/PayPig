using Dalamud.Configuration;

namespace PayPig;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Add your persisted settings here.
    public bool IsEnabled { get; set; } = false;

    // Max gil to send in a single trade. Defaults to 1,000,000.
    public uint MaxGilPerTrade { get; set; } = 1_000_000;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
