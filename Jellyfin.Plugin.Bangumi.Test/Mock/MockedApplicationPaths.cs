using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public class MockedApplicationPaths : IApplicationPaths
{
    private static readonly string BasePath = Util.FakePath.Create("application-data");
    private readonly string _basePath;

    public MockedApplicationPaths() : this(BasePath)
    {
    }

    public MockedApplicationPaths(string basePath)
    {
        _basePath = basePath;
    }

    public void MakeSanityCheckOrThrow()
    {
        throw new System.NotImplementedException();
    }

    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
        throw new System.NotImplementedException();
    }

    public string ProgramDataPath => _basePath;
    public string WebPath => _basePath;
    public string ProgramSystemPath => _basePath;
    public string DataPath => _basePath;
    public string ImageCachePath => _basePath;
    public string PluginsPath => _basePath;
    public string PluginConfigurationsPath => _basePath;
    public string LogDirectoryPath => _basePath;
    public string ConfigurationDirectoryPath => _basePath;
    public string SystemConfigurationFilePath => _basePath;
    public string CachePath => _basePath;
    public string TempDirectory => _basePath;
    public string VirtualDataPath => _basePath;

    public string TrickplayPath => _basePath;

    public string BackupPath => _basePath;
}
