using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Extensions
{
    [TestMethod]
    public void DictionaryGetOrDefault()
    {
        Dictionary<string, string> dictionary = new()
        {
            ["existed"] = "value"
        };

        Assert.AreEqual("value", dictionary.GetOrDefault("existed"));
        Assert.AreEqual(null, dictionary.GetOrDefault("not-existed"));
    }
}