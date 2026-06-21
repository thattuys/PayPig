using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PayPig.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private readonly Whitelist whitelist;

    private int selectedIndex = -1;
    private string statusMessage = string.Empty;

    public ConfigWindow(Plugin plugin) : base("PayPig Settings##Config")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
        whitelist = plugin.Whitelist;
        Size = new Vector2(600, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawLeftPane();
        ImGui.SameLine();
        DrawRightPane();
    }

    private void DrawLeftPane()
    {
        // Fixed-width left column: character list on top, add button pinned bottom.
        ImGui.BeginChild("##left", new Vector2(200, 0), true, ImGuiWindowFlags.None);

        var buttonHeight = ImGui.GetFrameHeightWithSpacing();
        var listHeight = ImGui.GetContentRegionAvail().Y - buttonHeight;

        ImGui.BeginChild("##list", new Vector2(0, listHeight), false, ImGuiWindowFlags.None);
        var entries = whitelist.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var label = $"{entry.Name} - {Plugin.GetWorldName(entry.WorldId)}##{i}";
            if (ImGui.Selectable(label, selectedIndex == i, ImGuiSelectableFlags.None, Vector2.Zero))
                selectedIndex = i;
        }
        ImGui.EndChild();

        if (ImGui.Button("Add target to WL", new Vector2(-1, 0)))
        {
            if (plugin.TryAddCurrentTargetToWhitelist(out var message))
                selectedIndex = whitelist.Entries.Count - 1;
            statusMessage = message;
        }

        ImGui.EndChild();
    }

    private void DrawRightPane()
    {
        ImGui.BeginChild("##right", new Vector2(0, 0), true, ImGuiWindowFlags.None);

        var entries = whitelist.Entries;
        if (selectedIndex < 0 || selectedIndex >= entries.Count)
        {
            ImGui.TextDisabled("Select a character on the left.");
            if (statusMessage.Length > 0)
            {
                ImGui.Separator();
                ImGui.TextWrapped(statusMessage);
            }
            ImGui.EndChild();
            return;
        }

        var entry = entries[selectedIndex];

        ImGui.TextUnformatted($"{entry.Name} - {Plugin.GetWorldName(entry.WorldId)}");
        ImGui.Separator();

        ImGui.TextUnformatted("Daily Limit:");
        ImGui.SameLine();
        var limit = (int)entry.Limit;
        ImGui.SetNextItemWidth(160);
        // step/stepFast 0 => no +/- buttons.
        if (ImGui.InputInt("##limit", ref limit, 0, 0, "%d", ImGuiInputTextFlags.None))
            entry.Limit = (uint)Math.Max(0, limit);
        if (ImGui.IsItemDeactivatedAfterEdit())
            whitelist.Save();
        ImGui.TextDisabled("0 = no daily limit");

        if (entry.Limit > 0)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var sentToday = entry.Date == today ? entry.Sent : 0u;
            ImGui.Spacing();
            ImGui.TextUnformatted($"Sent today: {sentToday:N0} / {entry.Limit:N0}");
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Remove from whitelist", new Vector2(-1, 0)))
        {
            whitelist.Remove(entry);
            statusMessage = $"Removed {entry.Name}.";
            selectedIndex = -1;
        }

        ImGui.EndChild();
    }
}
