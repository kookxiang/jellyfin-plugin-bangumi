using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class LocalConfigurationTestCases
{
    [TestMethod]
    public async Task Save()
    {
        var path = FakePath.CreateFile("save.ini");
        var config = new LocalConfiguration { Offset = 1 };
        await config.SaveTo(path);

        var content = await File.ReadAllTextAsync(path);
        Assert.IsTrue(content.Contains("Offset=1"), "changed value should be saved to file");
        Assert.IsFalse(content.Contains("Report="), "default value should not be saved to file");
    }

    [TestMethod]
    public async Task Load()
    {
        var path = FakePath.CreateFile("load.ini", "[Bangumi]\nOffset=1");
        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(config.Offset, 1, "should use configured value for report property");
        Assert.AreEqual(config.Report, true, "should use default for report property");
    }

    [TestMethod]
    public async Task Default()
    {
        var path = FakePath.CreateFile("not-exist.ini");
        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(config.Offset, 0, "should use default for offset property");
        Assert.AreEqual(config.Report, true, "should use default for report property");
    }
}