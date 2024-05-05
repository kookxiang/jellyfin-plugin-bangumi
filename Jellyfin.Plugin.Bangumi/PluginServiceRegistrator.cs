using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Bangumi;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<BangumiApi>();
        serviceCollection.AddSingleton<OAuthStore>();

        serviceCollection.AddHostedService<PlaybackScrobbler>();
    }
}