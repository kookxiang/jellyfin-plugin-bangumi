using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Providers;
using Jellyfin.Plugin.Bangumi.Test.Util;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Music
{
    private readonly AlbumProvider _albumProvider = ServiceLocator.GetService<AlbumProvider>();
    private readonly MusicArtistProvider _artistProvider = ServiceLocator.GetService<MusicArtistProvider>();
    private readonly MusicSongProvider _songProvider = ServiceLocator.GetService<MusicSongProvider>();

    private readonly CancellationToken _token = new();

    [TestMethod]
    public void ProviderInfo()
    {
        Assert.AreEqual(_albumProvider.Name, Constants.ProviderName);
        Assert.IsTrue(_albumProvider.Order < 0);
        Assert.AreEqual(_artistProvider.Name, Constants.ProviderName);
        Assert.IsTrue(_artistProvider.Order < 0);
        Assert.AreEqual(_songProvider.Name, Constants.ProviderName);
        Assert.IsTrue(_songProvider.Order < 0);
    }

    [TestMethod]
    public async Task AlbumInfo()
    {
        // 结城友奈は勇者である -鷲尾須美の章- オリジナルサウンドトラック
        const int subId = 214265;
        var result = await _albumProvider.GetMetadata(new AlbumInfo
        {
            Name = "结城友奈は勇者である -鷲尾須美の章- オリジナルサウンドトラック",
            ProviderIds = new Dictionary<string, string> { { Constants.ProviderName, subId.ToString() } }
        }, _token);

        Assert.IsNotNull(result.Item, "album metadata should not be null");
        Assert.IsFalse(string.IsNullOrEmpty(result.Item.Name));
        Assert.IsTrue(result.Item.AlbumArtists.Any(), "should contain artists");
    }

    [TestMethod]
    public async Task ArtistSearch()
    {
        var results = await _artistProvider.GetSearchResults(new ArtistInfo
        {
            Name = "澤野弘之"
        }, _token);

        Assert.IsNotNull(results, "search results should not be null");
        Assert.IsTrue(results.Any(), "should return at least one result");
        Assert.IsTrue(results.Any(r => r.Name.Contains("澤野弘之")), "should find Hiroyuki Sawano");
        Assert.IsNotNull(results.First().ProviderIds[Constants.ProviderName], "should have bangumi id");
    }

    [TestMethod]
    public async Task SongMatching()
    {
        const int albumId = 214265;
        
        var song1 = await _songProvider.GetSong(new SongInfo
        {
            Name = "舞台少女",
            IndexNumber = 1,
            ParentIndexNumber = 1,
            Path = FakePath.CreateFile("Yuyuyu/01. 舞台少女.flac")
        }, albumId, _token);
        
        Assert.IsNotNull(song1, "song 1 should be matched");
        Assert.AreEqual(805573, song1.Id);
    }

    [TestMethod]
    public async Task SongMatchingByName()
    {
        const int albumId = 214265;

        // 测试名称匹配优先：即便 IndexNumber 和 ParentIndexNumber 都不对，只要名字对也能匹配上
        var song = await _songProvider.GetSong(new SongInfo
        {
            Name = "わたしたちは", // 这是 Order 11 的歌
            IndexNumber = 99, // 错误的轨道号
            ParentIndexNumber = 9, // 错误的碟片号
            Path = FakePath.CreateFile("Yuyuyu/99. わたしたちは.flac")
        }, albumId, _token);

        Assert.IsNotNull(song, "song should be matched by name");
        Assert.AreEqual(805583, song.Id, "should match episode 805583 even with wrong indexes");
    }
}
