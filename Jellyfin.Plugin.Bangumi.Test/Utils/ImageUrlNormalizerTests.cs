using System.Collections.Generic;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
    public void RecognizesBangumiNoIconImage()
    {
        Assert.IsTrue(ImageUrlNormalizer.IsNoIconSubjectImage("http://lain.bgm.tv/img/no_icon_subject.png"));
        Assert.IsFalse(ImageUrlNormalizer.IsNoIconSubjectImage("http://lain.bgm.tv/pic/cover/l/ab/cd/123.jpg"));
    }

    [TestMethod]
    public void MissingLargeImageDoesNotThrow()
    {
        var subject = new Subject
        {
            Images = new Dictionary<string, string>
            {
                ["common"] = "http://lain.bgm.tv/pic/cover/c/ab/cd/123.jpg"
            }
        };

        Assert.IsNull(subject.DefaultImage);
    }
}
