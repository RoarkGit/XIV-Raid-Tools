using Dalamud.Configuration;

namespace XIVRaidToolsPlugin;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Empty means "use SessionClient.DefaultWsUrl" — lets a self-hosted
    // relay (or a local dev server) be swapped in without a rebuild.
    public string RelayUrlOverride { get; set; } = "";
}
