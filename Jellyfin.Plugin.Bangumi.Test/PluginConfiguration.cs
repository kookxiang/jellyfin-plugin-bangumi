using Jellyfin.Plugin.Bangumi.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class PluginConfigurationTestCases
{
    [TestMethod]
    public void BaseWebUrlDefault()
    {
        var config = new PluginConfiguration();
        Assert.AreEqual("https://bgm.tv", config.BaseWebUrl);
    }

    [TestMethod]
    public void NormalizeBaseWebUrl()
    {
        Assert.AreEqual("https://bgm.tv", PluginConfiguration.NormalizeBaseWebUrl(null));
        Assert.AreEqual("https://bgm.tv", PluginConfiguration.NormalizeBaseWebUrl("   "));
        Assert.AreEqual("https://example.com", PluginConfiguration.NormalizeBaseWebUrl("https://example.com/"));
    }
}
