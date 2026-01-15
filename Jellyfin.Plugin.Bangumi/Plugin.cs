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
    internal static Plugin? Instance;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => Constants.PluginName;

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(Constants.PluginGuid);

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Configuration",
                DisplayName = "Bangumi 设置",
                MenuIcon = "app_registration",
                EnableInMainMenu = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.Main.html"
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Configuration.Script",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.Main.js"
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Configuration.Style",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.Style.css"
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Tools.DuplicatedEpisodesDetector",
                DisplayName = "重复剧集检测",
                EmbeddedResourcePath = $"{GetType().Namespace}.Tools.DuplicatedEpisodesDetector.Index.html"
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Tools.DuplicatedEpisodesDetector.Script",
                EmbeddedResourcePath = $"{GetType().Namespace}.Tools.DuplicatedEpisodesDetector.Script.js"
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Tools.DuplicatedEpisodesDetector.Style",
                EmbeddedResourcePath = $"{GetType().Namespace}.Tools.DuplicatedEpisodesDetector.Style.css"
            },
        ];
    }
}
