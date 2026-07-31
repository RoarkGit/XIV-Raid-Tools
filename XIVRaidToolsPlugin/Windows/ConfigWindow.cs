using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace XIVRaidToolsPlugin.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Configuration _config;
    private readonly Action _save;

    public ConfigWindow(Configuration config, Action save) : base("XIV Raid Tools Settings##XrtConfig")
    {
        _config = config;
        _save = save;
        Size = new Vector2(440, 90);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void PreDraw() => Theme.PushWindowChrome();
    public override void PostDraw() => Theme.PopWindowChrome();

    public override void Draw()
    {
        var showTooltips = _config.ShowTooltips;
        if (ImGui.Checkbox("Show hover tooltips", ref showTooltips))
        {
            _config.ShowTooltips = showTooltips;
            _save();
        }
    }
}
