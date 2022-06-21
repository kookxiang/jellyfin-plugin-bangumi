using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Bangumi;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<Plugin>();
        serviceCollection.AddSingleton<BangumiApi>();
        serviceCollection.AddSingleton<OAuthStore>();
    }
}