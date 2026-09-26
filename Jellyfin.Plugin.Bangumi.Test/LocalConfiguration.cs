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
        await File.WriteAllTextAsync(path, "[Bangumi]\nOffset=3\n[Section.1]\nSelector=[某字幕组][**].mp4\nOffset=26\nSkip=on\nReport=off\nCorrectIndex=on\nType=Special\nID=123\n[Section.2]\nSelector=*.mp4\nOffset=0\n");

        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(26, config.GetOffset(firstFile));
        Assert.AreEqual(0, config.GetOffset(secondFile));
        Assert.AreEqual(3, config.GetOffset(unmatchedFile));
        Assert.AreEqual(3, config.GetOffset(null));

        var selected = await LocalConfiguration.ForPath(firstFile);
        Assert.AreEqual(26, selected.Offset);
        Assert.IsTrue(selected.Skip);
        Assert.IsFalse(selected.Report);
        Assert.IsTrue(selected.CorrectIndex);
        Assert.AreEqual(DirectoryType.Special, selected.Type);
        Assert.AreEqual(123, selected.Id);
        var second = await LocalConfiguration.ForPath(secondFile);
        Assert.AreEqual(0, second.Offset);
        Assert.IsFalse(second.Skip);
        Assert.IsTrue(second.Report);

        await config.SaveTo(path);
        StringAssert.Contains(await File.ReadAllTextAsync(path), "[Section.1]\nSelector=[某字幕组][**].mp4");
        var reloaded = new LocalConfiguration();
        await reloaded.ReadFrom(path);
        Assert.AreEqual(26, reloaded.GetOffset(firstFile));
        Assert.AreEqual(0, reloaded.GetOffset(secondFile));
        Assert.AreEqual(3, reloaded.GetOffset(unmatchedFile));
        Assert.AreEqual(2, reloaded.Sections.Count);
    }

    [TestMethod]
    public async Task InvalidSectionPropertyDoesNotOverrideDirectoryOffset()
    {
        var path = FakePath.CreateFile("invalid-selector.ini", "[Bangumi]\nOffset=4\n[Section.1]\nSelector=*.mp4\nOffset=invalid\n");
        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(4, config.GetOffset("[某字幕组][01].mp4"));
        Assert.AreEqual(1, config.Sections.Count);
    }

    [TestMethod]
    public async Task UnknownSectionDoesNotOverrideDirectoryOffset()
    {
        var path = FakePath.CreateFile("unknown-section.ini", "[Bangumi]\nOffset=3\n[Unrelated]\nOffset=26\n");
        var config = new LocalConfiguration();
        await config.ReadFrom(path);
        Assert.AreEqual(3, config.GetOffset("[某字幕组][27].mp4"));
    }

    [TestMethod]
    public async Task GlobalValuesBeforeFirstSectionNeedNoBangumiHeader()
    {
        var file = FakePath.CreateFile("no-bangumi-header/[某字幕组][01].mp4");
        var path = Path.Join(Path.GetDirectoryName(file), "bangumi.ini");
        await File.WriteAllTextAsync(path, "Offset=3\nReport=off\n[Section.1]\nSelector=[某字幕组]*.mp4\nSkip=on\n");

        var selected = await LocalConfiguration.ForPath(file);
        Assert.AreEqual(3, selected.Offset);
        Assert.IsFalse(selected.Report);
        Assert.IsTrue(selected.Skip);
    }

}
