using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PayPig.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("PayPig##Main")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted("PayPig is loaded.");
        ImGui.Separator();

        if (Plugin.PlayerState.IsLoaded)
            ImGui.TextUnformatted($"Logged in as: {Plugin.PlayerState.CharacterName}");
        else
            ImGui.TextUnformatted("Not logged in.");

        ImGui.Spacing();
        var enabled = plugin.Configuration.IsEnabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            plugin.Configuration.IsEnabled = enabled;
            plugin.Configuration.Save();
        }

        ImGui.Spacing();
        var publicDrain = plugin.Configuration.PublicDrain;
        if (ImGui.Checkbox("Public Drain Mode(You trade to anyone with no limits)", ref publicDrain))
        {
            plugin.Configuration.PublicDrain = publicDrain;
            plugin.Configuration.Save();
        }

        ImGui.Spacing();
        if (ImGui.Button("Open Settings"))
            plugin.ToggleConfigUi();
    }
}
