using System;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.Offline;
using Jellyfin.Plugin.Bangumi.Test.Util;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test;

[TestClass]
public class Database
{
    private readonly PluginDatabase _database = ServiceLocator.GetService<PluginDatabase>();

    [TestInitialize]
    public void DownloadFromArchive()
    {
        if (_database.Subjects.Count() > 0) return;
        Console.WriteLine("database was empty, import data from github archive");
        var task = ServiceLocator.GetService<ArchiveDataDownloadTask>();
        task.ExecuteAsync(new Progress<double>(), CancellationToken.None).Wait();
    }

    [TestMethod]
    public void Subject()
    {
        var subject = _database.Subjects.FindOne(x => x.Id == 364450);
        Assert.IsNotNull(subject);
        Assert.AreEqual("リコリス・リコイル", subject.OriginalName);
    }

    [TestMethod]
    public void Episode()
    {
        var subject = _database.Episodes.FindOne(x => x.Id == 1111258);
        Assert.IsNotNull(subject);
        Assert.AreEqual("Easy does it", subject.OriginalName);
    }

    [TestMethod]
    public void EpisodeList()
    {
        var episodes = _database.Episodes.Find(x => x.ParentId == 364450);
        Assert.AreEqual(13, episodes.Count(x => x.Type == EpisodeType.Normal));
    }
}