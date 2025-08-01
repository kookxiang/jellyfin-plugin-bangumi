﻿using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public class MockedApplicationPaths : IApplicationPaths
{
    public void MakeSanityCheckOrThrow()
    {
        throw new System.NotImplementedException();
    }

    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
        throw new System.NotImplementedException();
    }

    public string ProgramDataPath => Path.GetTempPath();
    public string WebPath => Path.GetTempPath();
    public string ProgramSystemPath => Path.GetTempPath();
    public string DataPath => Path.GetTempPath();
    public string ImageCachePath => Path.GetTempPath();
    public string PluginsPath => Path.GetTempPath();
    public string PluginConfigurationsPath => Path.GetTempPath();
    public string LogDirectoryPath => Path.GetTempPath();
    public string ConfigurationDirectoryPath => Path.GetTempPath();
    public string SystemConfigurationFilePath => Path.GetTempPath();
    public string CachePath => Path.GetTempPath();
    public string TempDirectory => Path.GetTempPath();
    public string VirtualDataPath => Path.GetTempPath();

    public string TrickplayPath => Path.GetTempPath();

    public string BackupPath => Path.GetTempPath();
}
