using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Logging;

namespace Jellyfin.Plugin.Bangumi;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogManager logger) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        if (logger != null)
        {
            Log = logger.GetLogger(this.Name);
        }
    }
    public static ILogger Log { get; set; }

    public override string Name => Constants.PluginName;

    public override Guid Id => Guid.Parse(Constants.PluginGuid);

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = Constants.ProviderName,
                DisplayName = Constants.PluginName,
                MenuIcon = "app_registration",
                MenuSection = "server",
                EnableInMainMenu = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.ConfigPage.html"
            },
            new PluginPageInfo
            {
                Name = "BangumiJS",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.ConfigPage.js"
            }
        };
    }
}