using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class InfoBoxTest
{
    private readonly BangumiApi _api = ServiceLocator.GetService<BangumiApi>();

    private readonly CancellationToken _token = CancellationToken.None;

    [TestMethod]
    public async Task DuplicatedKey()
    {
        var subject = await _api.GetSubject(374319, _token);
        Assert.AreEqual(subject?.InfoBox?.Get("中文名"), "无意间变成狗，被喜欢的女生捡回家。");
    }
}
