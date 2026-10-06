using Jellyfin.Plugin.Bangumi.Archive;
using System;
using System.Net.Http;
using Jellyfin.Plugin.Bangumi.AI;
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
        serviceCollection.AddSingleton<AiStatisticsStore>();
        serviceCollection.AddHttpClient<AiProviderClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.MaxResponseContentBufferSize = 64 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        serviceCollection.AddSingleton<ArchiveData>();
        serviceCollection.AddSingleton<BangumiApi>();
        serviceCollection.AddSingleton<OAuthStore>();
        serviceCollection.AddSingleton<OAuthAuthorizationStore>();
        serviceCollection.AddSingleton<UserSettingsInjectionService>();

        serviceCollection.AddHostedService<PlaybackScrobbler>();
        serviceCollection.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<UserSettingsInjectionService>());
    }
}
