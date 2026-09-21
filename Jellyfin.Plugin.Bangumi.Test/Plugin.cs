using System.Linq;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Plugin
{
    [TestMethod]
    public void PluginInfo()
    {
        var plugin = ServiceLocator.GetService<Bangumi.Plugin>()!;
        Assert.AreEqual(Constants.PluginGuid, plugin.Id.ToString(), "should have plugin id");
        Assert.AreEqual(Constants.PluginName, plugin.Name, "should have plugin name");
        Assert.IsTrue(plugin.GetPages().Any(), "should have plugin pages");
        Assert.IsFalse(
            plugin.GetPages().Any(page => page.Name == "Plugin.Bangumi.Tools.MediaLibrary"),
            "media library manager should be embedded in the settings page");
    }
}
