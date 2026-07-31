using Dalamud.Configuration;

namespace XIVRaidToolsPlugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool ShowTooltips { get; set; } = true;
}
