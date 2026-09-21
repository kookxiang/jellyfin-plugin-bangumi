using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class WebResourceTestCases
{
    [DataTestMethod]
    [DataRow("DuplicatedEpisodesDetector", "bangumi-tool-duplicates")]
    [DataRow("FixEpisodeMetadata", "bangumi-tool-fix-metadata")]
    [DataRow("MissingBangumiId", "bangumi-tool-missing-id")]
    public void LegacyToolRoutesEmbedFrontendEntries(string resource, string component)
    {
        var assembly = typeof(global::Jellyfin.Plugin.Bangumi.Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Jellyfin.Plugin.Bangumi.Tools.{resource}.Index.html");
        Assert.IsNotNull(stream);
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();
        StringAssert.Contains(html, $"tool=\"{component}\"");
        StringAssert.Contains(html, "configurationpage?name=Plugin.Bangumi.Configuration.Script");
    }

    [TestMethod]
    public void ConfigurationEmbedsProductionEntryAndBundle()
    {
        var assembly = typeof(global::Jellyfin.Plugin.Bangumi.Plugin).Assembly;
        using var htmlStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.Bangumi.Configuration.Main.html");
        Assert.IsNotNull(htmlStream);
        using var htmlReader = new StreamReader(htmlStream);
        var html = htmlReader.ReadToEnd();
        StringAssert.Contains(html, "<jellyfin-plugin-bangumi>");
        StringAssert.Contains(html, "data-title=\"Bangumi\"");
        StringAssert.Contains(html, "configurationpage?name=Plugin.Bangumi.Configuration.Script");

        using var scriptStream = assembly.GetManifestResourceStream("Jellyfin.Plugin.Bangumi.Configuration.Main.js");
        Assert.IsNotNull(scriptStream);
        using var scriptReader = new StreamReader(scriptStream);
        var script = scriptReader.ReadToEnd();
        StringAssert.Contains(script, "window.ApiClient");
        StringAssert.Contains(script, "window.Dashboard");
        StringAssert.Contains(script, "bangumi-oauth-container");
        Assert.IsFalse(script.Contains("test-user"), "Mock users must not be shipped in the plugin.");
        Assert.IsFalse(script.Contains("sample-1"), "Mock episodes must not be shipped in the plugin.");
        Assert.IsFalse(html.Contains("8765"), "Production must not depend on the preview server.");
    }
}
