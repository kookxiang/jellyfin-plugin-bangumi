using System.Collections.Generic;
using Jellyfin.Plugin.Bangumi.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelPerson = Jellyfin.Plugin.Bangumi.Model.Person;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class ImageUrlNormalizerTests
{
    [TestMethod]
    public void ConvertsBangumiHttpImageUrlToHttps()
    {
        const string url = "http://lain.bgm.tv/pic/cover/l/ab/cd/123.jpg?r=1";

        Assert.AreEqual(
            "https://lain.bgm.tv/pic/cover/l/ab/cd/123.jpg?r=1",
            ImageUrlNormalizer.Normalize(url));
    }

    [TestMethod]
    public void LeavesNonBangumiImageUrlsUnchanged()
    {
        const string url = "http://images.example.com/poster.jpg";

        Assert.AreEqual(url, ImageUrlNormalizer.Normalize(url));
    }

    [TestMethod]
    public void NormalizesModelImageProperties()
    {
        var subject = new Subject
        {
            Images = new Dictionary<string, string>
            {
                ["large"] = "http://lain.bgm.tv/pic/cover/l/ab/cd/123.jpg"
            }
        };
        var person = new ModelPerson
        {
            Images = new Dictionary<string, string>
            {
                ["large"] = "http://lain.bgm.tv/pic/crt/l/ab/cd/456.jpg"
            }
        };

        Assert.AreEqual("https://lain.bgm.tv/pic/cover/l/ab/cd/123.jpg", subject.DefaultImage);
        Assert.AreEqual("https://lain.bgm.tv/pic/crt/l/ab/cd/456.jpg", person.DefaultImage);
    }
}
