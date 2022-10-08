using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace Jellyfin.Plugin.Bangumi.Offline;

public class JsonLinesProvider<T> : IEnumerable<T>
{
    private readonly Stream _stream;

    public JsonLinesProvider(Stream stream)
    {
        _stream = stream;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return new JsonLinesEnumerator<T>(_stream);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}