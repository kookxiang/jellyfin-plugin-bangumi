using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Jellyfin.Plugin.Bangumi.Offline;

public class JsonLinesEnumerator<T> : IEnumerator<T>
{
    private readonly JsonSerializerOptions? _options;
    private readonly StreamReader _reader;

    public JsonLinesEnumerator(Stream stream)
    {
        _reader = new StreamReader(stream);
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public T Current { get; private set; } = default!;

    object IEnumerator.Current => Current!;

    public void Dispose()
    {
        _reader.Dispose();
        GC.SuppressFinalize(this);
    }

    public bool MoveNext()
    {
        var jsonString = _reader.ReadLine();
        if (string.IsNullOrEmpty(jsonString))
            return false;
        Current = JsonSerializer.Deserialize<T>(jsonString, _options)!;
        Thread.Yield();
        return true;
    }

    public void Reset()
    {
        _reader.BaseStream.Seek(0, SeekOrigin.Begin);
    }
}