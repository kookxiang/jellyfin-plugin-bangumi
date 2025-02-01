using System;
using System.IO;
using System.Xml.Serialization;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Bangumi.Test.Mock;

public class MockedXmlSerializer : IXmlSerializer
{
    public object? DeserializeFromBytes(Type type, byte[] buffer)
    {
        using var stream = new MemoryStream(buffer);
        return DeserializeFromStream(type, stream);
    }

    public object? DeserializeFromFile(Type type, string file)
    {
        using var stream = new FileStream(file, FileMode.Open);
        return DeserializeFromStream(type, stream);
    }

    public object? DeserializeFromStream(Type type, Stream stream)
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
