using System;
using System.IO;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Jellyfin.Plugin.Bangumi.Test.Util;

[TestClass]
public class FakePath
{
    private static readonly string BasePath = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString());

    public static string Create(string name)
    {
        var path = Path.Join(BasePath, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateFile(string name, string content = "")
    {
        var path = Path.Join(BasePath, name);
        var parent = Path.GetDirectoryName(path);
        if (parent != null)
            Directory.CreateDirectory(parent);
        File.Create(path).Close();
        if (!string.IsNullOrEmpty(content))
            File.WriteAllText(path, content);
        return path;
    }

    public static MediaBrowser.Controller.Entities.TV.Series CreateSeries(ILibraryManager libraryManager, string path)
    {
        var item = new MediaBrowser.Controller.Entities.TV.Series()
        {
            Path = Create(path)
        };
        libraryManager.CreateItem(item, null);

        return item;
    }

    public static MediaBrowser.Controller.Entities.TV.Season CreateSeason(ILibraryManager libraryManager, string path)
    {
        var item = new MediaBrowser.Controller.Entities.TV.Season()
        {
            Path = Create(path)
        };
        libraryManager.CreateItem(item, null);

        return item;
    }

    public static void CreateLocalConfiguration(string path, LocalConfiguration config)
    {
        var fullpath = Create(path);
        var filePath = Path.Join(fullpath, "bangumi.ini");

        config?.SaveTo(filePath).Wait();
    }

    [AssemblyCleanup]
    public static void CleanupFakePath()
    {
        if (Directory.Exists(BasePath))
            Directory.Delete(BasePath, true);
    }
}
