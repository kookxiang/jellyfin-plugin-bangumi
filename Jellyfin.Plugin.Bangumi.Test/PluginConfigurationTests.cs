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
    public void UserSettingsInjectionIsDisabledByDefault()
    {
        var configuration = new PluginConfiguration();

        Assert.IsFalse(configuration.EnableUserSettingsInjection);
    }
}
