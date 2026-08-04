using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using XIVRaidToolsPlugin.Windows;

namespace XIVRaidToolsPlugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/xrt";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private readonly WindowSystem _windowSystem = new("XIVRaidTools");
    private readonly Configuration _configuration;
    private readonly ConfigWindow _configWindow;
    private readonly KefkaSaysWindow _kefkaWindow;
    private readonly PullHistoryWindow _historyWindow;
    private readonly SessionClient<MechState> _session;

    public Plugin()
    {
        _configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        _session = new SessionClient<MechState>(Log, new MechState());
        _kefkaWindow = new KefkaSaysWindow(_session, new GameIcons(DataManager, TextureProvider), _configuration);
        _configWindow = new ConfigWindow(_configuration, () => PluginInterface.SavePluginConfig(_configuration));
        _historyWindow = new PullHistoryWindow(_session, _kefkaWindow);
        _kefkaWindow.HistoryWindow = _historyWindow;
        _kefkaWindow.SettingsWindow = _configWindow;
        // KefkaSaysWindow must be added (and therefore Draw, refreshing
        // CurrentPos/CurrentSize) before PullHistoryWindow each frame, so
        // the popout's anchor position is never a frame stale.
        _windowSystem.AddWindow(_kefkaWindow);
        _windowSystem.AddWindow(_historyWindow);
        _windowSystem.AddWindow(_configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open XIV Raid Tools, or fire a call as if its button were clicked.\n"
                + "  /xrt kefka\n"
                + "    gco [real|fake|water|lightning|bomb]\n"
                + "    gco1|gco2 [real|fake|water|lightning|bomb]\n"
                + "    <tsunami|inferno|thunder|blizzard> [real|fake]\n"
                + "    reset\n"
                + "  /xrt config\n"
                + "Bare gco/tsunami/inferno commands assume order of occurrence (first call sets GCO1/Floor AOE #1, second sets GCO2/Floor AOE #2). Use gco1/gco2 to target one explicitly instead.",
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += () => _kefkaWindow.IsOpen = true;
        PluginInterface.UiBuilder.OpenConfigUi += () => _configWindow.IsOpen = true;

        // A wrong password, a taken custom room code, etc. used to only hit
        // the plugin log (see ReportInvalidCommand's comment below), so
        // nobody actually trying to join would ever see why it failed.
        _session.SessionError += msg => ChatGui.PrintError($"XIV Raid Tools: {msg}");
    }

    // A Log.Warning alone is invisible in practice - it only reaches the
    // Dalamud plugin log, not the game chat or the window, so a bad macro
    // fired mid-pull would silently no-op with no on-screen indication why.
    // PrintError puts the same message in chat, in the game's standard
    // error-red, so it's visible without digging through /xllog.
    private static void ReportInvalidCommand(string message)
    {
        Log.Warning(message);
        ChatGui.PrintError(message);
    }

    // Single "/xrt" command routes to whichever tool is named first (only
    // Kefka Says exists so far, plus the "config" pseudo-tool for settings).
    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var tool = parts.Length > 0 ? parts[0].ToLowerInvariant() : "kefka";
        var rest = parts.Length > 1 ? parts[1] : "";

        switch (tool)
        {
            case "config":
                _configWindow.IsOpen = true;
                break;

            case "kefka":
                HandleKefkaCommand(rest);
                break;

            default:
                ReportInvalidCommand($"XIV Raid Tools: unknown tool '{tool}'. Usage: /xrt kefka, /xrt config");
                break;
        }
    }

    // Lets a call be fired from the command line (macro/hotkey friendly)
    // exactly as if the matching button in the window had been clicked:
    // same toggle-off-on-repeat semantics, same PushState() calls. No
    // subcommand carries a GCO index (1 or 2) since there's no clean way to
    // express "which one" in a macro without hardcoding pull order, so gco
    // instead targets whichever slot isn't resolved yet on the relevant axis
    // (see HandleGco's comment).
    private void HandleKefkaCommand(string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            _kefkaWindow.IsOpen = true;
            return;
        }

        var parts = trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var sub = parts[0].ToLowerInvariant();
        var arg1 = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";
        var s = _session.State;

        switch (sub)
        {
            case "gco":
                if (!HandleGco(arg1)) return;
                break;

            case "gco1":
                if (!HandleGcoExplicit(1, arg1)) return;
                break;

            case "gco2":
                if (!HandleGcoExplicit(2, arg1)) return;
                break;

            case "tsunami" when arg1 is "real" or "fake":
                HandleFloor(FloorType.Tsunami, ParseRf(arg1));
                break;

            case "inferno" when arg1 is "real" or "fake":
                HandleFloor(FloorType.Inferno, ParseRf(arg1));
                break;

            case "thunder" when arg1 is "real" or "fake":
                SetRfIdempotent(ref s.ThunderRf, ParseRf(arg1));
                _session.PushState();
                break;

            case "blizzard" when arg1 is "real" or "fake":
                SetRfIdempotent(ref s.BlizzardRf, ParseRf(arg1));
                _session.PushState();
                break;

            case "reset":
            {
                var snapshot = s.Reset();
                _session.PushState(payload =>
                {
                    payload["clearDebuffs"] = true;
                    if (snapshot is not null) payload["historySnapshot"] = MechState.SerializeSnapshot(snapshot);
                });
                break;
            }

            default:
                ReportInvalidCommand($"XIV Raid Tools: unrecognized command '{trimmed}'. {HelpText}");
                return;
        }

        _kefkaWindow.IsOpen = true;
    }

    private const string HelpText = "Usage: /xrt kefka gco [real|fake|water|lightning|bomb], "
        + "/xrt kefka <tsunami|inferno|thunder|blizzard> [real|fake], /xrt kefka reset";

    private static RF ParseRf(string arg) => arg == "real" ? RF.Real : RF.Fake;

    // Unlike the window's own click handlers (SetRf/SetIt1Rf/SetIt2Rf, the
    // Thunder/Blizzard closures - see KefkaSaysWindow), which deliberately
    // toggle off on a second click, slash commands set a Cast value
    // idempotently: calling the same value again just confirms it, it never
    // un-calls it. A manual click toggling off on repeat is a reasonable
    // affordance, but a macro/trigger can legitimately fire the exact same
    // command many times for what's logically one game event - confirmed by
    // a real Telesto EasyTrigger log showing 13 identical
    // "/xrt kefka gco bomb" sends in under 2 seconds for a single debuff
    // application. Toggling off on repeat there would mean the outcome
    // depends on whether that burst happens to land on an odd or even
    // count, which is exactly what was happening. Only the opposite value
    // or Reset changes it away from an already-set value now.
    private static void SetRfIdempotent(ref RF field, RF value) => field = value;

    // "gco real"/"gco fake" targets whichever GCO's Cast is still unresolved
    // (G1 first, then G2) - once both are set, there's nothing left to call
    // until Reset. "gco water|lightning|bomb" does the same but keyed off
    // the debuff fields instead: since SetPos/ToggleAccel's pairing rule
    // (see MechState.cs) auto-derives the OTHER target's assignment, only
    // one debuff call is ever needed per GCO pair. Both no-op (reporting an
    // error, not opening the window - see caller) once both slots on that
    // axis are already resolved. Returns whether the command actually did
    // anything, so HandleKefkaCommand knows whether to open the window.
    private bool HandleGco(string arg)
    {
        var s = _session.State;
        switch (arg)
        {
            case "real" or "fake":
            {
                var gco = s.G1Rf == RF.None ? 1 : s.G2Rf == RF.None ? 2 : 0;
                if (gco == 0)
                {
                    ReportInvalidCommand("XIV Raid Tools: both GCO casts are already called, reset first. "
                        + "Use gco1/gco2 to target a specific one directly instead.");
                    return false;
                }
                return ApplyGcoRf(gco, arg);
            }

            case "water" or "lightning" or "bomb":
            {
                var gco = s.G1Pos == Pos.None && !s.G1Accel ? 1 : s.G2Pos == Pos.None && !s.G2Accel ? 2 : 0;
                if (gco == 0)
                {
                    // Both slots are already resolved - but SetPos auto-derives
                    // the OTHER slot's assignment the instant either water or
                    // lightning is called (see MechState.SetPos's comment: one
                    // target gets the debuff, the other necessarily gets the
                    // bomb), so a single water/lightning call alone always
                    // reaches this state. If what's being called now is
                    // exactly what that auto-derivation already produced, this
                    // isn't a conflicting call, just a redundant confirmation
                    // (e.g. the bomb-haver calling "bomb" after a teammate's
                    // water call already implied it) - treat that as a
                    // harmless no-op instead of an error, same as the RF
                    // race guard's "confirm, don't complain" treatment.
                    var alreadyConsistent = arg switch
                    {
                        "water" => s.G1Pos == Pos.Water || s.G2Pos == Pos.Water,
                        "lightning" => s.G1Pos == Pos.Lightning || s.G2Pos == Pos.Lightning,
                        "bomb" => s.G1Accel || s.G2Accel,
                        _ => false,
                    };
                    if (alreadyConsistent) return true;

                    ReportInvalidCommand("XIV Raid Tools: both GCO debuffs are already assigned, reset first. "
                        + "Use gco1/gco2 to target a specific one directly instead.");
                    return false;
                }
                return ApplyGcoDebuff(gco, arg);
            }

            default:
                ReportInvalidCommand("XIV Raid Tools: usage is /xrt kefka gco [real|fake|water|lightning|bomb]");
                return false;
        }
    }

    // "gco1"/"gco2" bypass the order-of-occurrence inference entirely and
    // target that exact GCO, for when a macro needs to be explicit (e.g.
    // firing GCO2 first because that's how a particular pull went) rather
    // than relying on "whichever isn't resolved yet". No "already resolved"
    // guard here - toggling a specific, known slot is always well-defined.
    private bool HandleGcoExplicit(int gco, string arg) => arg switch
    {
        "real" or "fake" => ApplyGcoRf(gco, arg),
        "water" or "lightning" or "bomb" => ApplyGcoDebuff(gco, arg),
        _ => ReportGcoUsage(),
    };

    private bool ReportGcoUsage()
    {
        ReportInvalidCommand("XIV Raid Tools: usage is /xrt kefka gco1|gco2 [real|fake|water|lightning|bomb]");
        return false;
    }

    private bool ApplyGcoRf(int gco, string arg)
    {
        var s = _session.State;
        var v = ParseRf(arg);
        if (gco == 1) SetRfIdempotent(ref s.G1Rf, v); else SetRfIdempotent(ref s.G2Rf, v);
        _session.PushState();
        return true;
    }

    // No PushState() - g1pos/g2pos/g1accel/g2accel are never in
    // BuildSharedState's payload (personal, unsynced fields), so pushing
    // after only one of these changing would send the room's already-synced
    // fields unchanged. Matches KefkaSaysWindow's DebuffsRow buttons.
    //
    // gco1/gco2 (explicit) route straight here with no "already resolved"
    // gate (see HandleGcoExplicit's comment - unlike the auto-pick "gco"
    // path, there's no ambiguity about which slot to target). But SetPos's
    // auto-derivation (see its own comment) can leave a slot's Accel true
    // without THIS command ever having been called for it - a redundant
    // explicit confirmation of that already-correct value would otherwise
    // hit ToggleAccel/SetPos's unconditional flip and un-set it. Guard each
    // case against its own already-matching value first, same "confirm,
    // don't corrupt" treatment as the auto-pick path's alreadyConsistent
    // check above.
    private bool ApplyGcoDebuff(int gco, string arg)
    {
        var s = _session.State;
        var pos = gco == 1 ? s.G1Pos : s.G2Pos;
        var accel = gco == 1 ? s.G1Accel : s.G2Accel;
        switch (arg)
        {
            case "water": if (pos != Pos.Water) s.SetPos(gco, Pos.Water); break;
            case "lightning": if (pos != Pos.Lightning) s.SetPos(gco, Pos.Lightning); break;
            case "bomb": if (!accel) s.ToggleAccel(gco); break;
        }
        return true;
    }

    // "tsunami"/"inferno" name a floor shape, not a slot (1 or 2) - Floor AOE
    // #2's type is always the complement of #1's (see MechState.It2Type), so
    // the first call of either name claims slot 1 and fixes what slot 2 must
    // be; a later call naming the OTHER shape then targets slot 2 instead.
    private void HandleFloor(FloorType type, RF value)
    {
        var s = _session.State;
        if (s.It1Type == FloorType.None) s.It1Type = type;

        if (s.It1Type == type) SetRfIdempotent(ref s.It1Rf, value);
        else SetRfIdempotent(ref s.It2Rf, value);

        _session.PushState();
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _windowSystem.RemoveAllWindows();
        _session.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }
}
