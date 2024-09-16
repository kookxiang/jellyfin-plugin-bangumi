using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Mock;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test.Util;

[TestClass]
public class ServiceLocator
{
    private static ServiceProvider _provider;

    [AssemblyInitialize]
    public static void Init(TestContext context)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHttpClient();
        serviceCollection.AddLogging(builder => builder.AddConsole());
        serviceCollection.AddSingleton<IXmlSerializer, MockedXmlSerializer>();
        serviceCollection.AddSingleton<IApplicationPaths, MockedApplicationPaths>();
        serviceCollection.AddSingleton<ILibraryManager, MockedLibraryManager>();
        serviceCollection.AddScoped<Bangumi.Plugin>();
        serviceCollection.AddScoped<BookProvider>();
        serviceCollection.AddScoped<EpisodeProvider>();
        serviceCollection.AddScoped<MovieProvider>();
        serviceCollection.AddScoped<PersonProvider>();
        serviceCollection.AddScoped<PersonImageProvider>();
        serviceCollection.AddScoped<SeasonProvider>();
        serviceCollection.AddScoped<SeriesProvider>();
        serviceCollection.AddScoped<SubjectImageProvider>();
        new PluginServiceRegistrator().RegisterServices(serviceCollection, null!);
        _provider = serviceCollection.BuildServiceProvider();

        var plugin = GetService<Bangumi.Plugin>();
        plugin.Configuration.TranslationPreference = TranslationPreferenceType.Original;
        plugin.Configuration.SeasonGuessMaxSearchCount = 10;
    }

    public static T GetService<T>()
    {
        return _provider.GetService<T>();
    }
}