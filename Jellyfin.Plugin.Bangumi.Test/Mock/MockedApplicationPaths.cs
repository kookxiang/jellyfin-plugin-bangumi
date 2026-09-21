using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public class MockedApplicationPaths : IApplicationPaths
{
    private static readonly string BasePath = Util.FakePath.Create("application-data");
    public void MakeSanityCheckOrThrow()
    {
        throw new System.NotImplementedException();
    }

    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
        throw new System.NotImplementedException();
    }

    public string ProgramDataPath => BasePath;
    public string WebPath => BasePath;
    public string ProgramSystemPath => BasePath;
    public string DataPath => BasePath;
    public string ImageCachePath => BasePath;
    public string PluginsPath => BasePath;
    public string PluginConfigurationsPath => BasePath;
    public string LogDirectoryPath => BasePath;
    public string ConfigurationDirectoryPath => BasePath;
    public string SystemConfigurationFilePath => BasePath;
    public string CachePath => BasePath;
    public string TempDirectory => BasePath;
    public string VirtualDataPath => BasePath;

    public string TrickplayPath => BasePath;

    public string BackupPath => BasePath;
}
