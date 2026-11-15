using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Subgen;

/// <summary>
/// Registers the Subgen subtitle provider with Jellyfin.
/// Jellyfin 12 only uses subtitle providers that are registered here.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ISubtitleProvider, SubgenSubtitleProvider>();
        serviceCollection.AddSingleton<SubtitleWatcher>();
        serviceCollection.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, WebInjectorStartupFilter>();
    }
}
