using System.IO;
using Jellyfin.Plugin.Bangumi.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class PluginConfigurationTests
{
    [TestMethod]
    public void PreferAnimeSearchIsEnabledByDefault()
    {
        var configuration = new PluginConfiguration();

        Assert.IsTrue(configuration.PreferAnimeSearch);
    }

    [TestMethod]
    [DoNotParallelize]
    public void PluginStartupMigratesOldConfigurationAndPreservesLaterOptOut()
    {
        var previousPlugin = Bangumi.Plugin.Instance;
        var paths = new Mock.MockedApplicationPaths(Util.FakePath.Create("legacy-plugin-configuration"));
        var serializer = new Mock.MockedXmlSerializer();

        try
        {
            var freshPlugin = new Bangumi.Plugin(paths, serializer);
            Assert.IsFalse(freshPlugin.Configuration.ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp);

            var configurationPath = freshPlugin.ConfigurationFilePath;
            File.WriteAllText(configurationPath, "<PluginConfiguration><EpisodeParser>AnitomySharp</EpisodeParser></PluginConfiguration>");

            var upgradedPlugin = new Bangumi.Plugin(paths, serializer);
            Assert.IsTrue(upgradedPlugin.Configuration.ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp);
            Assert.IsTrue(File.ReadAllText(configurationPath).Contains("<ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp>true</ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp>"));

            upgradedPlugin.Configuration.ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp = false;
            upgradedPlugin.SaveConfiguration();

            var reopenedPlugin = new Bangumi.Plugin(paths, serializer);
            Assert.IsFalse(reopenedPlugin.Configuration.ProcessMultiSeasonWithConsecutiveIndexByAnitomySharp);
        }
        finally
        {
            Bangumi.Plugin.Instance = previousPlugin;
        }
    }
}
