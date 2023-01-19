using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Game.Gui;
using System;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState;
using System.Runtime.InteropServices;
using Dalamud.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace PayPig;

public unsafe class Plugin : IDalamudPlugin
{
    public string Name => "Pay Pig";
    Configuration _configuration;

    [PluginService]
    [RequiredVersion("1.0")]
    public static DalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService]
    [RequiredVersion("1.0")]
    public static CommandManager CommandManager { get; set; } = null!;

    [PluginService]
    [RequiredVersion("1.0")]
    public static ObjectTable ObjectTable { get; set; } = null!;

    [PluginService]
    [RequiredVersion("1.0")]
    public static ClientState ClientState { get; set; } = null!;

    [PluginService]
    [RequiredVersion("1.0")]
    public static ChatGui ChatGui { get; set; } = null!;

    [PluginService]
    [RequiredVersion("1.0")]
    public static GameGui GameGui { get; set; } = null!;

    private static string DrainModeCommand => "/gildrain";
    private static string AddFinDomCommand => "/giladdowner";
    private static string SetMacroCommand => "/gilmacro";

    public static nint emoteAgent = nint.Zero;
    public delegate void DoEmoteDelegate(nint agent, uint emoteID, long a3, bool a4, bool a5);
    public static DoEmoteDelegate? DoEmote;

    private bool doGrovel;

    public Plugin()
    {
        _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _configuration.Initialize(PluginInterface);

        CommandManager.AddHandler(
            DrainModeCommand,
            new CommandInfo(this.DrainMode) {
                HelpMessage = "Allows anyone to take the piggies money",
                ShowInHelp = true
        });

        CommandManager.AddHandler(
            AddFinDomCommand,
            new CommandInfo(this.AddFinDom) {
                HelpMessage = "Get on your knees, look at the person that better deserves your money and run this to allow them to take what is theirs",
                ShowInHelp = true
        });

        CommandManager.AddHandler(
            SetMacroCommand,
            new CommandInfo(this.SetMacro) {
                HelpMessage = "Sets the piggies macro to give their gil to the rightful owner",
                ShowInHelp = true
        });

        var sigScanner = new SigScanner();

        var agentModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->GetUiModule()->GetAgentModule();
        DoEmote = Marshal.GetDelegateForFunctionPointer<DoEmoteDelegate>(sigScanner.ScanText("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? B8 0A 00 00 00"));
        emoteAgent = (nint)agentModule->GetAgentByInternalId(AgentId.Emote);

        ChatGui.ChatMessage += WatchTradeRequest;

        doGrovel = false;
    }

    public void WatchTradeRequest(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (isHandled)
        {
            return;
        }
        if (type == (XivChatType)569)
        {
            var playerPayload = message.Payloads[0] as PlayerPayload;
            if ( _configuration.inDrainMode
                || (playerPayload != null && this._configuration.FinDommies.Any(
                    x =>
                        x.Name == playerPayload.PlayerName
                        && x.HomeworldName == playerPayload.World.Name
                    )
                )
            ) {
                var agentInterface = GameGui.FindAgentInterface("Trade");
                if (agentInterface == IntPtr.Zero) return;
                var agent = (AgentInterface*)agentInterface;
                if (agent->IsAgentActive())
                {
                    if (_configuration.isSharedMacro) {
                        RaptureShellModule.Instance->ExecuteMacro((RaptureMacroModule.Instance->Shared)[_configuration.Macro]);
                    } else {
                        RaptureShellModule.Instance->ExecuteMacro((RaptureMacroModule.Instance->Individual)[_configuration.Macro]);
                    }
                    doGrovel = true;
                }
            }
        } else if (type == XivChatType.SystemMessage && message.TextValue == "Trade complete." && doGrovel) {
            if (DoEmote != null)
                DoEmote(emoteAgent, 47, 0, true, true);
            doGrovel = false;
        }
    }

    public void DrainMode(string command, string arguments)
    {
        _configuration.inDrainMode = !_configuration.inDrainMode;
        if (_configuration.inDrainMode) {
            ChatGui.Print("[PayPig] You are now in drain mode. Enjoy walking~");
        } else {
            ChatGui.Print("[PayPig] Aww is the piggy drained?");
        }
        this._configuration.Save();
    }

    public void AddFinDom(string command, string arguments)
    {
        if (ObjectTable.SingleOrDefault(
                x => x is PlayerCharacter
                    && x.ObjectId != 0
                    && x.ObjectId != ClientState.LocalPlayer?.ObjectId
                    && x.ObjectId == ClientState.LocalPlayer?.TargetObjectId) is PlayerCharacter actor) {
        
            var item = new FinDoms(actor);
            if (!this._configuration.FinDommies.Any(
                x => x.Name == item.Name && x.HomeworldName == item.HomeworldName
            )) {
                this._configuration.FinDommies.Add(item);
            }
            ChatGui.Print("[PayPig] Good piggy, now let them know and stand there and look pitiful as your wallet is drained.");
        } else {
            ChatGui.Print("[PayPig] Poor wittle piggy cannot even find their superior.");
        }
        this._configuration.Save();
    }

    public void SetMacro(string command, string arguments)
    {
        bool isDumbPiggy = false;
        var splitArgs = arguments.Split(" ");
        if (splitArgs.Length != 2) {
            isDumbPiggy = true;
        } else {
            if (splitArgs[0].ToLower() != "individual" && splitArgs[0] != "shared") {
                isDumbPiggy = true;
            } else {
                Int32 macroNumber = 0;
                if (Int32.TryParse(splitArgs[1], out macroNumber) == false) {
                    isDumbPiggy = true;
                } else {
                    if (macroNumber < 0 || macroNumber > 99) {
                        isDumbPiggy = true;
                    } else {
                        _configuration.Macro = macroNumber;
                        _configuration.isSharedMacro = (splitArgs[0].ToLower() == "shared");
                    }
                }
            }
        }

        if (isDumbPiggy) {
            ChatGui.Print("[PayPig] Poor dumb piggy. Do you need your dommy to hold you hand for this?");
        } else {
            ChatGui.Print("[PayPig] Macro set");
            this._configuration.Save();
        }
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= WatchTradeRequest;
        CommandManager.RemoveHandler(DrainModeCommand);
        CommandManager.RemoveHandler(AddFinDomCommand);
        CommandManager.RemoveHandler(SetMacroCommand);
    }
}
