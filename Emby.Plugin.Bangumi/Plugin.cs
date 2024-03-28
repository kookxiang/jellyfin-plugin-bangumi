using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Bangumi;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

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