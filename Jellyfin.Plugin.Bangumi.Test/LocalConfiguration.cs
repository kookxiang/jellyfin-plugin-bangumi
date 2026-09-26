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

    [TestMethod]
    public async Task InvalidIntegerUsesDefault()
    {
        var path = FakePath.CreateFile("invalid-integer.ini", "[Bangumi]\nID=invalid\nOffset=2");
        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(0, config.Id, "invalid integer value should be ignored");
        Assert.AreEqual(2, config.Offset, "valid values should still be loaded");
    }
    [DataTestMethod]
    [DataRow("Auto", DirectoryType.Auto)]
    [DataRow("normal", DirectoryType.Normal)]
    [DataRow("Special", DirectoryType.Special)]
    [DataRow("unknown", DirectoryType.Auto)]
    [DataRow("999", DirectoryType.Auto)]
    public async Task DirectoryTypeRoundTrip(string value, DirectoryType expected)
    {
        var path = FakePath.CreateFile($"type-{value}.ini", $"[Bangumi]\nType={value}");
        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(expected, config.Type);
        await config.SaveTo(path);
        var saved = await File.ReadAllTextAsync(path);
        if (expected == DirectoryType.Auto)
            Assert.IsFalse(saved.Contains("Type="));
        else
            StringAssert.Contains(saved, $"Type={expected}");
        var reloaded = new LocalConfiguration();
        await reloaded.ReadFrom(path);
        Assert.AreEqual(expected, reloaded.Type);
    }

    [TestMethod]
    public async Task FileSelectorsUseFirstMatchAndFallbackToDirectoryOffset()
    {
        var firstFile = FakePath.CreateFile("offset-rules/[某字幕组][01].mp4");
        var secondFile = FakePath.CreateFile("offset-rules/[其他字幕组][01].mp4");
        var unmatchedFile = FakePath.CreateFile("offset-rules/episode-01.mkv");
        var path = Path.Join(Path.GetDirectoryName(firstFile), "bangumi.ini");
        await File.WriteAllTextAsync(path, "[Bangumi]\nOffset=3\n[File:[某字幕组][**].mp4]\nOffset=26\n[File:*.mp4]\nOffset=0\n");

        var config = await LocalConfiguration.ForPath(firstFile);
        Assert.AreEqual(26, config.GetOffset(firstFile));
        Assert.AreEqual(0, config.GetOffset(secondFile));
        Assert.AreEqual(3, config.GetOffset(unmatchedFile));
        Assert.AreEqual(3, config.GetOffset(null));

        await config.SaveTo(path);
        var reloaded = await LocalConfiguration.ForPath(firstFile);
        Assert.AreEqual(26, reloaded.GetOffset(firstFile));
        Assert.AreEqual(0, reloaded.GetOffset(secondFile));
        Assert.AreEqual(3, reloaded.GetOffset(unmatchedFile));
        Assert.AreEqual(2, reloaded.OffsetRules.Count);
    }

    [TestMethod]
    public async Task InvalidFileSectionDoesNotOverrideDirectoryOffset()
    {
        var path = FakePath.CreateFile("invalid-selector.ini", "[Bangumi]\nOffset=4\n[File:*.mp4]\nOffset=invalid\n");
        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(4, config.GetOffset("[某字幕组][01].mp4"));
        Assert.AreEqual(0, config.OffsetRules.Count);
    }

}
