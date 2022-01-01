using System;
using System.IO;
using System.Net.Http;
using System.Xml.Serialization;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Bangumi.Test
{
    public class TestApplicationPaths : IApplicationPaths
    {
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
    }

    public class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    public class TestXmlSerializer: IXmlSerializer
    {
        public object DeserializeFromBytes(Type type, byte[] buffer)
        {
            using var stream = new MemoryStream(buffer);
            return DeserializeFromStream(type, stream);
        }

        public object DeserializeFromFile(Type type, string file)
        {
            using var stream = new FileStream(file, FileMode.Open);
            return DeserializeFromStream(type, stream);
        }

        public object DeserializeFromStream(Type type, Stream stream)
        {
            var serializer = new XmlSerializer(type);
            return serializer.Deserialize(stream);
        }

        public void SerializeToFile(object obj, string file)
        {
            using var stream = new FileStream(file, FileMode.OpenOrCreate);
            SerializeToStream(obj, stream);
        }

        public void SerializeToStream(object obj, Stream stream)
        {
            var serializer = new XmlSerializer(obj.GetType());
            serializer.Serialize(stream, obj);
        }
    }
}