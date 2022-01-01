using Jellyfin.Plugin.Bangumi.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test
{
    [TestClass]
    public class PluginInitializer
    {
        [AssemblyInitialize]
        public static void Init(TestContext context)
        {
            _ = new Plugin(new TestApplicationPaths(), new TestXmlSerializer(), new TestHttpClientFactory())
            {
                Configuration =
                {
                    TranslationPreference = TranslationPreferenceType.Original
                }
            };
        }
    }
}