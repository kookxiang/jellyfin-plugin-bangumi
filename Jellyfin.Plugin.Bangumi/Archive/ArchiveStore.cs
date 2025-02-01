using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Archive;

public interface IArchiveStore
{
    public string FileName { get; }

    public string FilePath { get; }

    public bool Exists();

    public Task GenerateIndex(CancellationToken token);

    public Task Move(string newBasePath, string newFileName);

    public IArchiveStore Fork(string newBasePath, string newFileName);
}

public partial class ArchiveStore<T>(string basePath, string fileName) : IArchiveStore
    where T : class
{
    private string IndexFilePath => Path.ChangeExtension(FilePath, ".idx");

    public string BasePath { get; private set; } = basePath;

    public string FilePath => Path.Combine(BasePath, FileName);

    public string FileName { get; private set; } = fileName;

    public bool Exists()
    {
        return File.Exists(IndexFilePath) && File.Exists(FilePath);
    }

    public async Task GenerateIndex(CancellationToken token)
    {
        using var reader = new StreamReader(FilePath, Encoding.UTF8);

        reader.BaseStream.Seek(-64 * 1024, SeekOrigin.End);
        var data = await reader.ReadToEndAsync(token);
        if (!LineIdRegex().IsMatch(data))
            throw new FormatException("cannot locate id of last record");
        var lastId = int.Parse(LineIdRegex().Matches(data).Last().Groups[1].Value);

        var indexSize = lastId switch
        {
            < byte.MaxValue => sizeof(byte),
            < ushort.MaxValue => sizeof(ushort),
            _ => sizeof(uint)
        };

        // use memory stream to avoid frequent disk i/o
        await using var memoryStream = new MemoryStream(lastId * indexSize);
        memoryStream.WriteByte((byte)indexSize);
        var startPosition = 0L;
        reader.BaseStream.Seek(startPosition, SeekOrigin.Begin);
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (!LineIdRegex().IsMatch(line))
                continue;
            var id = int.Parse(LineIdRegex().Match(line).Groups[1].Value);

            memoryStream.Seek(id * indexSize, SeekOrigin.Begin);
            var offsetData = indexSize switch
            {
                sizeof(byte) => [(byte)startPosition],
                sizeof(ushort) => BitConverter.GetBytes((ushort)startPosition),
                sizeof(uint) => BitConverter.GetBytes((uint)startPosition),
                _ => throw new FormatException("invalid index size")
            };
            await memoryStream.WriteAsync(offsetData, token);
            startPosition += Encoding.UTF8.GetByteCount(line + "\n");
        }

        // save memory stream to disk
        await using var fileStream = File.Create(IndexFilePath);
        fileStream.SetLength(memoryStream.Length);
        memoryStream.WriteTo(fileStream);
        await fileStream.FlushAsync(token);
    }

    public async Task Move(string newBasePath, string newFileName)
    {
        var store = (ArchiveStore<T>)Fork(newBasePath, newFileName);
        await Task.Run(() =>
        {
            if (File.Exists(FilePath))
                File.Move(FilePath, store.FilePath);
            if (File.Exists(IndexFilePath))
                File.Move(IndexFilePath, store.IndexFilePath);
        });
        BasePath = store.FilePath;
        FileName = store.IndexFilePath;
    }

    public IArchiveStore Fork(string newBasePath, string newFileName)
    {
        return new ArchiveStore<T>(newBasePath, newFileName);
    }

    public IEnumerable<T> Enumerate()
    {
        if (!Exists())
            yield break;

        using var reader = new StreamReader(FilePath, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line == null)
                continue;
            if (!LineIdRegex().IsMatch(line))
                continue;
            yield return JsonSerializer.Deserialize<T>(line, Constants.JsonSerializerOptions)!;
        }
    }

    public async Task<T?> FindById(int id)
    {
        if (!Exists())
            return null;

        var indexInfo = new FileInfo(IndexFilePath);
        if (indexInfo.Length == 0)
            return null;
        await using var indexReader = File.OpenRead(IndexFilePath);
        var indexSize = indexReader.ReadByte();
        if (indexSize is not (sizeof(byte) or sizeof(ushort) or sizeof(uint)))
            throw new FormatException("invalid index size");
        if (indexInfo.Length < id * indexSize)
            return null;

        indexReader.Seek(id * indexSize, SeekOrigin.Begin);
        var buffer = new byte[indexSize];
        // ReSharper disable once MustUseReturnValue
        await indexReader.ReadAsync(buffer.AsMemory(0, indexSize));
        var offset = indexSize switch
        {
            sizeof(byte) => buffer[0],
            sizeof(ushort) => BitConverter.ToUInt16(buffer),
            sizeof(uint) => BitConverter.ToUInt32(buffer),
            _ => throw new FormatException("invalid index size")
        };

        var fileInfo = new FileInfo(FilePath);
        if (fileInfo.Length == 0 || fileInfo.Length < offset)
            return null;
        using var textReader = new StreamReader(FilePath);
        textReader.BaseStream.Seek(offset, SeekOrigin.Begin);
        var line = await textReader.ReadLineAsync();
        return line == null ? null : JsonSerializer.Deserialize<T>(line, Constants.JsonSerializerOptions);
    }

    [GeneratedRegex("\"id\":\\s*(\\d+)")]
    private static partial Regex LineIdRegex();
}
