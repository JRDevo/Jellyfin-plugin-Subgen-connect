using Jellyfin.Plugin.Subgen.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Subgen;

/// <summary>
/// Subgen plugin: adds "Generate with Subgen" to Jellyfin's subtitle search.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Subgen Connect";

    public override Guid Id => Guid.Parse("5b9f3c1e-7a2d-4e8b-9c41-2f6d8a1b3e70");

    public override string Description => "Connects Jellyfin to your own Subgen server to generate subtitles on demand.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
