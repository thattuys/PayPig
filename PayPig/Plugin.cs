using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using System.Text.RegularExpressions;
using PayPig.Windows;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PayPig;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/paypig";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public Whitelist Whitelist { get; init; }

    public readonly WindowSystem WindowSystem = new("PayPig");
    private MainWindow MainWindow { get; init; }
    private ConfigWindow ConfigWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Whitelist = new Whitelist(Path.Combine(PluginInterface.ConfigDirectory.FullName, "whitelist.json"));

        MainWindow = new MainWindow(this);
        ConfigWindow = new ConfigWindow(this);

        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the PayPig menu.\n" +
                        "/paypig whitelist → Opens the whitelist manager.\n" +
                        "/paypig toggle → Toggles paypig on or off."
        });

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        Framework.Update += OnFrameworkUpdate;
        ChatGui.ChatMessage += OnChatMessage;

        // Load confirmation — appears in /xllog. If you DON'T see this after a
        // reload, the new build isn't actually loaded.
        Log.Information("PayPig loaded.");
    }

    // "You hand over 1,000,000 gil." — the definitive amount actually sent.
    private static readonly Regex HandOverGil =
        new(@"You hand over ([\d,]+) gil", RegexOptions.Compiled);

    // "<name> wishes to trade with you." — triggered when someone else opens a trade with us.
    private static readonly Regex TradeRequest =
        new(@"(.+) wishes to trade with you\.", RegexOptions.Compiled);

    // Node ids of the trade window buttons (from the addon inspector).
    // NOTE: assumed 33 = Trade, 34 = Cancel — swap if behavior is inverted.
    private const uint CancelButtonNodeId = 34;
    private const uint TradeButtonNodeId = 33;

    // The close (X) button is node 7, nested inside the window component (node 35).
    private const uint WindowNodeId = 35;
    private const uint CloseButtonNodeId = 7;

    // Frames to wait after opening before auto-clicking Trade, so the gil we
    // set has registered. -1 = nothing pending.
    private int framesUntilConfirm = -1;

    private bool isTradeActive = false;
    private bool lockCurrentTrade = false;   // true while enforcing a whitelisted trade
    private WhitelistEntry? pendingEntry;     // partner of the in-progress trade
    private bool incomingTradeRequest = false; // true when someone else opened trade with us

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        var trade = GetTradeAddon();
        var open = trade != null;

        // Edge detection: only act on the transition, not every frame.
        // Only trigger auto-trade logic if this was an incoming trade request.
        if (open && !isTradeActive && incomingTradeRequest)
            OnTradeOpened(trade);
        else if (!open && isTradeActive)
            OnTradeClosed();

        isTradeActive = open;

        // `trade` is valid for THIS frame only — never cache the pointer.
        if (open && lockCurrentTrade && Configuration.IsEnabled)
        {
            // Keep the Cancel button disabled and the close (X) button hidden
            // so we can't back out ourselves.
            SetButtonEnabled(trade, CancelButtonNodeId, false);
            SetNestedNodeVisible(trade, WindowNodeId, CloseButtonNodeId, false);

            // After a short delay (gil registered), auto-click the Trade button.
            if (framesUntilConfirm > 0)
            {
                framesUntilConfirm--;
            }
            else if (framesUntilConfirm == 0)
            {
                framesUntilConfirm = -1; // one-shot
                if (ClickButton(trade, TradeButtonNodeId))
                    Log.Information("Auto-clicked the Trade button.");
                else
                    framesUntilConfirm = 5;
            }
        }
    }

    /// <summary>
    /// Simulates a click on a button component node by re-dispatching its own
    /// registered ButtonClick event back through the addon. No-op if the node
    /// is missing or the button is disabled.
    /// </summary>
    private unsafe bool ClickButton(AddonTrade* trade, uint nodeId)
    {
        if (nodeId == 0)
        {
            Log.Warning("Trade button NodeID is not set (0) — fill in TradeButtonNodeId.");
            return false;
        }

        var node = trade->AtkUnitBase.GetNodeById(nodeId);
        if (node == null)
        {
            Log.Warning("ClickButton: node {Id} not found in the Trade addon.", nodeId);
            return false;
        }

        var button = node->GetAsAtkComponentButton();
        if (button == null)
        {
            Log.Warning("ClickButton: node {Id} is not a button component.", nodeId);
            return false;
        }

        if (!button->IsEnabled)
        {
            Log.Warning("ClickButton: button {Id} is currently disabled.", nodeId);
            return false;
        }

        // Find the button's own ButtonClick event so the param matches what the
        // addon's handler expects, then feed it back in.
        var evt = node->AtkEventManager.Event;
        while (evt != null && evt->State.EventType != AtkEventType.ButtonClick)
            evt = evt->NextEvent;
        if (evt == null)
        {
            Log.Warning("ClickButton: no ButtonClick event registered on node {Id}.", nodeId);
            return false;
        }

        var data = new AtkEventData();
        trade->AtkUnitBase.ReceiveEvent(AtkEventType.ButtonClick, (int)evt->Param, evt, &data);
        return true;
    }

    /// <summary>Enable/disable a button component node. No-op if the node isn't found.</summary>
    private unsafe void SetButtonEnabled(AddonTrade* trade, uint nodeId, bool enabled)
    {
        if (nodeId == 0)
            return;

        var node = trade->AtkUnitBase.GetNodeById(nodeId);
        if (node == null)
            return;

        var button = node->GetAsAtkComponentButton();
        if (button == null)
            return;

        button->SetEnabledState(enabled);
    }

    /// <summary>
    /// Show/hide a node nested inside a component node's own node list, e.g. the
    /// close (X) button (node 7) inside the window component (node 35).
    /// </summary>
    private unsafe void SetNestedNodeVisible(AddonTrade* trade, uint componentNodeId, uint childNodeId, bool visible)
    {
        var node = trade->AtkUnitBase.GetNodeById(componentNodeId);
        if (node == null)
            return;

        var component = node->GetAsAtkComponentNode();
        if (component == null || component->Component == null)
            return;

        var child = component->Component->UldManager.SearchNodeById(childNodeId);
        if (child == null)
            return;

        child->ToggleVisibility(visible);
    }

    /// <summary>
    /// Reads the integer shown in a text node nested inside a component node.
    /// e.g. ReadNodeNumber(trade, 13, 2) reads the send-gil text. Returns 0 if
    /// any node in the path is missing or the text isn't numeric.
    /// </summary>
    private unsafe uint ReadNodeNumber(AddonTrade* trade, uint componentNodeId, uint textNodeId)
    {
        var node = trade->AtkUnitBase.GetNodeById(componentNodeId);
        if (node == null)
            return 0;

        var component = node->GetAsAtkComponentNode();
        if (component == null || component->Component == null)
            return 0;

        // The text node lives in the component's own node list, not the
        // addon's top-level list — look it up via the component.
        var textNode = component->Component->GetTextNodeById(textNodeId);
        if (textNode == null)
            return 0;

        // Strip grouping separators / spaces; keep digits only.
        var text = textNode->NodeText.ToString();
        var digits = new string(text.Where(char.IsDigit).ToArray());
        return uint.TryParse(digits, out var value) ? value : 0u;
    }

    /// <summary>
    /// Re-resolves the Trade addon every call and validates it's actually
    /// open and ready. Returns null when there's no usable trade window.
    /// </summary>
    static private unsafe AddonTrade* GetTradeAddon()
    {
        // GetAddonByName now returns a managed AtkUnitBasePtr wrapper.
        var unitBase = GameGui.GetAddonByName("Trade", 1);
        // IsReady = fully constructed; IsVisible = currently shown.
        if (unitBase.IsNull || !unitBase.IsReady || !unitBase.IsVisible)
            return null;

        // Drop to the native struct pointer only when you need struct fields.
        return (AddonTrade*)unitBase.Address;
    }

    private unsafe void OnTradeOpened(AddonTrade* trade)
    {
        // Reset any prior trade state.
        lockCurrentTrade = false;
        pendingEntry = null;
        framesUntilConfirm = -1;
        incomingTradeRequest = false; // consumed

        // Global kill switch — do nothing while disabled.
        if (!Configuration.IsEnabled)
            return;

        // Who are we trading with?
        var partner = GetTradePartner();
        if (partner is null)
        {
            Log.Warning("Trade opened but the partner could not be resolved.");
            return;
        }

        var (name, worldId) = partner.Value;

        // Whitelist gate: only auto-send to people on the list.
        var entry = Whitelist.Find(name, worldId);
        if (entry is null && !Configuration.PublicDrain)
        {
            ChatGui.Print($"[PayPig] {name} (world {worldId}) is not whitelisted — no gil sent.");
            Log.Information("Trade with non-whitelisted {Name}@{World}.", name, worldId);
            return;
        }

        var inventory = InventoryManager.Instance();
        var gilHeld = inventory != null ? inventory->GetGil() : 0u;

        // Per-trade max, capped by what we hold and the remaining daily budget.
        var amount = Math.Min(Configuration.MaxGilPerTrade, gilHeld);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (!Configuration.PublicDrain && entry != null)
        {
            var remaining = Whitelist.RemainingToday(entry, today);
            if (remaining is uint dailyCap)
                amount = Math.Min(amount, dailyCap);

            Log.Information(
                "Trade with {Name}@{World}: set send gil to {Amount:N0} (daily remaining {Rem}).",
                name, worldId, amount, remaining?.ToString("N0") ?? "unlimited");
        }

        if (inventory != null)
            inventory->SetTradeGilAmount(amount);

        // Arm enforcement: lock the window (no self-cancel) and remember the
        // partner so we can record the daily total when the trade completes.
        lockCurrentTrade = true;
        pendingEntry = entry;
        framesUntilConfirm = 5; // let the gil register, then auto-click Trade

        ChatGui.Print($"[PayPig] {name}: set send gil to {amount:N0}.");
    }

    /// <summary>
    /// Resolves the current trade partner to (name, home-world id) via their
    /// entity id. Returns null if there's no partner or they aren't a player.
    /// </summary>
    private unsafe (string Name, uint WorldId)? GetTradePartner()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return null;

        var obj = ObjectTable.SearchByEntityId(inventory->TradePartnerEntityId);
        if (obj is IPlayerCharacter pc)
            return (pc.Name.TextValue, pc.HomeWorld.RowId);

        return null;
    }

    /// <summary>Resolves a world id to its server name, e.g. 91 -> "Lich".</summary>
    internal static string GetWorldName(uint worldId)
    {
        var row = DataManager.GetExcelSheet<Lumina.Excel.Sheets.World>().GetRowOrDefault(worldId);
        return row?.Name.ExtractText() ?? $"#{worldId}";
    }

    /// <summary>
    /// Adds the currently targeted player to the whitelist. Returns false (with
    /// a reason) if there's no player target or they're already listed.
    /// </summary>
    internal bool TryAddCurrentTargetToWhitelist(out string message)
    {
        if (TargetManager.Target is not IPlayerCharacter pc)
        {
            message = "No player targeted.";
            return false;
        }

        var name = pc.Name.TextValue;
        var worldId = pc.HomeWorld.RowId;

        if (Whitelist.Find(name, worldId) is not null)
        {
            message = $"{name} is already whitelisted.";
            return false;
        }

        Whitelist.Add(new WhitelistEntry { Name = name, WorldId = worldId, Limit = 0 });
        message = $"Added {name} - {GetWorldName(worldId)} to the whitelist.";
        return true;
    }

    private void OnTradeClosed()
    {
        // Stop disabling the Cancel button. We deliberately keep pendingEntry
        // set — the chat handler decides complete vs. canceled, and that
        // message can arrive on the same frame the window closes.
        lockCurrentTrade = false;
        framesUntilConfirm = -1;
        incomingTradeRequest = false; // reset on close
    }

    /// <summary>
    /// Settles the in-progress trade off chat. Only acts while a trade is armed
    /// (pendingEntry != null), so it can't false-trigger on idle chat:
    ///   "You hand over X gil." -> record X against the daily limit
    ///   "Trade complete." / "Trade canceled." -> clear the armed trade
    /// </summary>
    private void OnChatMessage(IHandleableChatMessage msg)
    {
        var text = msg.Message.TextValue;

        // Check for incoming trade request: "<name> wishes to trade with you."
        // Also check with simpler Contains for debugging
        if (text.Contains("wishes to trade with you", StringComparison.OrdinalIgnoreCase))
        {
            incomingTradeRequest = true;
            Log.Information("Incoming trade request detected in message: {Text}", text);
            ChatGui.Print($"[PayPig] Incoming trade request detected: {text}");
        }

        var tradeRequest = TradeRequest.Match(text);
        if (tradeRequest.Success)
        {
            Log.Information("Regex matched trade request from {Name}.", tradeRequest.Groups[1].Value);
        }

        // Handle gil tracking and trade completion for active trades
        if (pendingEntry is null && !text.Contains("wishes to trade", StringComparison.OrdinalIgnoreCase))
            return;

        var handOver = HandOverGil.Match(text);
        if (handOver.Success &&
            uint.TryParse(handOver.Groups[1].Value.Replace(",", ""), out var sent) && !Configuration.PublicDrain)
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            Whitelist.RecordSent(pendingEntry, sent, today);
            Log.Information("Recorded {Gil:N0} gil for {Name}.", sent, pendingEntry.Name);
            ChatGui.Print($"[PayPig] Recorded {sent:N0} gil for {pendingEntry.Name}.");
            return; // keep armed until the trade fully resolves below
        }

        // "cancel" matches both "canceled" and "cancelled" spellings.
        if (text.Contains("Trade complete", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Trade cancel", StringComparison.OrdinalIgnoreCase))
        {
            pendingEntry = null;
        }
    }

    public void Dispose()
    {

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        Framework.Update -= OnFrameworkUpdate;
        ChatGui.ChatMessage -= OnChatMessage;
    }

    private void OnCommand(string command, string args)
    {
        var arg = args.Trim().ToLower();
        if (arg == "whitelist")
            ToggleConfigUi();
        else if (arg == "toggle")
        {
            Configuration.IsEnabled = !Configuration.IsEnabled;
            Configuration.Save();
        }
        else
            ToggleMainUi();
    }

    private void DrawUi() => WindowSystem.Draw();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
