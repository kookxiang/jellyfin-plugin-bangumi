using System.Collections.Generic;
using Jellyfin.Plugin.Bangumi.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Extensions
{
    [TestMethod]
    public void EnumGetValue()
    {
        Assert.AreEqual("watched", EpisodeStatus.Watched.GetValue());
    }

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