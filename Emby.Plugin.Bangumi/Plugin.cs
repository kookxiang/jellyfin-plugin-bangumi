using System;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Bangumi;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public static Plugin? Instance;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => Constants.PluginName;

    public override Guid Id => Guid.Parse(Constants.PluginGuid);
}