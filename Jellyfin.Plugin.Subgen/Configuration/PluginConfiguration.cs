using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Subgen.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Base URL of the Subgen server.</summary>
    public string SubgenUrl { get; set; } = "http://localhost:9000";

    /// <summary>Maximum minutes to wait for one subtitle.</summary>
    public int TimeoutMinutes { get; set; } = 20;
}
