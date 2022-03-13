using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test
{
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
        }
    }
}