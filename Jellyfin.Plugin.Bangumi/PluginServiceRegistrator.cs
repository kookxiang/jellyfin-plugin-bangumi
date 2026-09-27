using Jellyfin.Plugin.Bangumi.Archive;
using Jellyfin.Plugin.Bangumi.OAuth;
using Jellyfin.Plugin.Bangumi.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Bangumi;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddScoped(typeof(Logger<>));

        serviceCollection.AddSingleton<ArchiveData>();
        serviceCollection.AddSingleton<BangumiApi>();
        serviceCollection.AddSingleton<OAuthStore>();
        serviceCollection.AddSingleton<OAuthAuthorizationStore>();
        serviceCollection.AddSingleton<UserSettingsInjectionService>();

        serviceCollection.AddHostedService<PlaybackScrobbler>();
        serviceCollection.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<UserSettingsInjectionService>());
    }
}
