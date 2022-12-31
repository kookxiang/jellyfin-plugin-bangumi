using System;
using System.IO;
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

    public static string CreateFile(string name)
    {
        var path = Path.Join(BasePath, name);
        var parent = Path.GetDirectoryName(path);
        if (parent != null)
            Directory.CreateDirectory(parent);
        File.Create(path).Close();
        return path;
    }

    [AssemblyCleanup]
    public static void CleanupFakePath()
    {
        if (Directory.Exists(BasePath))
            Directory.Delete(BasePath, true);
    }
}