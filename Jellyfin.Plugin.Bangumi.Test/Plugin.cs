using System.Linq;
using Jellyfin.Plugin.Bangumi.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test
{
    [TestClass]
    public class Plugin
    {
        [AssemblyInitialize]
        public static void Init(TestContext context)
        {
            _ = new Bangumi.Plugin(new TestApplicationPaths(), new TestXmlSerializer(), new TestHttpClientFactory())
            {
                Configuration =
                {
                    TranslationPreference = TranslationPreferenceType.Original
                }
            };
        }

        [TestMethod]
        public void PluginInfo()
        {
            var instance = Bangumi.Plugin.Instance;
            Assert.AreEqual(Constants.PluginGuid, instance.Id.ToString(), "should have plugin id");
            Assert.AreEqual(Constants.PluginName, instance.Name, "should have plugin name");
            Assert.IsTrue(instance.GetPages().Any(), "should have plugin pages");
        }
    }
}