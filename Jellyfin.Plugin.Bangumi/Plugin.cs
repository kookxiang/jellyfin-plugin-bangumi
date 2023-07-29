using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Bangumi;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin? Instance;
    
    public Dictionary<string, long> MediaTicks = new();

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        LoadCache();
        var harmony = new Harmony("jellyfin.plugin.bangumi");
        harmony.PatchAll();
    }

    /// <inheritdoc />
    public override string Name => Constants.PluginName;

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(Constants.PluginGuid);

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "Plugin.Bangumi.Configuration",
                DisplayName = "Bangumi 设置",
                MenuIcon = "app_registration",
                EnableInMainMenu = true,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.ConfigPage.html"
            }
        };
    }

    public void LoadCache()
    {
        var ticksPath = Path.Combine(ApplicationPaths.CachePath, "bangumi", "ticks.json");
        if (File.Exists(ticksPath))
            MediaTicks = JsonConvert.DeserializeObject<Dictionary<string, long>>(File.ReadAllText(ticksPath));
    }
    
    public void SaveCache()
    {
        var json = JsonConvert.SerializeObject(MediaTicks);
        var path = Path.Combine(ApplicationPaths.CachePath, "bangumi");
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "ticks.json"), json);
    }
}