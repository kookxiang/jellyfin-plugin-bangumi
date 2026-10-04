using Jellyfin.Plugin.Bangumi.Web;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class UserSettingsIndexTransformerTests
{
    [TestMethod]
    public void InjectionStatus_UsesStableCamelCaseApiFields()
    {
        var json = JsonSerializer.Serialize(new UserSettingsInjectionStatus(false, true, false, null));

        StringAssert.Contains(json, "\"enabled\":false");
        StringAssert.Contains(json, "\"dependencyAvailable\":true");
        Assert.IsFalse(json.Contains("\"DependencyAvailable\"", System.StringComparison.Ordinal));
    }

    [TestMethod]
    public void Transform_InsertsScriptBeforeBodyOnce()
    {
        const string html = "<!doctype html><html><body><main>Jellyfin</main></body></html>";

        var transformed = UserSettingsIndexTransformer.Transform(new JObject { ["contents"] = html });
        var repeated = UserSettingsIndexTransformer.Transform(new JObject { ["contents"] = transformed });

        StringAssert.Contains(transformed, UserSettingsIndexTransformer.Marker);
        StringAssert.Contains(transformed, UserSettingsIndexTransformer.Script);
        Assert.IsTrue(transformed.IndexOf(UserSettingsIndexTransformer.Script, System.StringComparison.Ordinal)
            < transformed.IndexOf("</body>", System.StringComparison.Ordinal));
        Assert.AreEqual(transformed, repeated);
    }

    [DataTestMethod]
    [DataRow("")]
    [DataRow("<html><main>missing body</main></html>")]
    public void Transform_LeavesUnsupportedContentUnchanged(string html)
    {
        Assert.AreEqual(html, UserSettingsIndexTransformer.Transform(new JObject { ["contents"] = html }));
    }
}
