using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
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
        if (MigrateLegacyMultiSeasonConfiguration(Configuration, ConfigurationFilePath))
            SaveConfiguration();
    }

    internal static bool MigrateLegacyMultiSeasonConfiguration(PluginConfiguration configuration, string configurationPath)
    {
        if (!File.Exists(configurationPath))
            return false;

        try
        {
            using var reader = XmlReader.Create(configurationPath, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element &&
                    reader.LocalName == nameof(PluginConfiguration.ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp))
                    return false;
            }
        }
        catch (XmlException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        // Older versions always tried this fallback when the basic Anitomy match failed.
        configuration.ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp = true;
        return true;
    }

    /// <inheritdoc />
    public override string Name => Constants.PluginName;

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(Constants.PluginGuid);

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var scriptResource = $"{GetType().Namespace}.Configuration.Main.js";
#if DEBUG
        if (WebDevelopmentController.ServerUrl is not null)
            scriptResource = $"{GetType().Namespace}.Configuration.Development.js";
#endif
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
                EmbeddedResourcePath = scriptResource
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Tools.DuplicatedEpisodesDetector",
                DisplayName = "重复剧集检测",
                EmbeddedResourcePath = $"{GetType().Namespace}.Tools.DuplicatedEpisodesDetector.Index.html"
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Tools.FixEpisodeMetadata",
                DisplayName = "修正错误的剧集元数据",
                EmbeddedResourcePath = $"{GetType().Namespace}.Tools.FixEpisodeMetadata.Index.html"
            },
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Tools.MissingBangumiId",
                DisplayName = "缺失 Bangumi ID 的视频",
                EmbeddedResourcePath = $"{GetType().Namespace}.Tools.MissingBangumiId.Index.html"
            },
        ];
    }
}
