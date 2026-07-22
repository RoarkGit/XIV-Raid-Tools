using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace XIVRaidToolsPlugin.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;
    private readonly Action _save;
    private string _relayUrlInput;

    public ConfigWindow(Configuration config, Action save) : base("XIV Raid Tools Settings##XrtConfig")
    {
        _config = config;
        _save = save;
        _relayUrlInput = config.RelayUrlOverride;
        Size = new Vector2(440, 130);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.PushWindowChrome();
    public override void PostDraw() => Theme.PopWindowChrome();

    public override void Draw()
    {
        ImGui.TextWrapped("Relay server URL. Leave blank to use the default public relay.");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputTextWithHint("##relayurl", SessionDefaults.DefaultWsUrl, ref _relayUrlInput, 256))
        {
            _config.RelayUrlOverride = _relayUrlInput;
            _save();
        }
    }
}
