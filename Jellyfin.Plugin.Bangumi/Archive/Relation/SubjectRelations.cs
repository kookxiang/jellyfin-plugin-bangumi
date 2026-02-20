using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Archive.Data;

namespace Jellyfin.Plugin.Bangumi.Archive.Relation;

public class SubjectRelations(ArchiveData archive)
{
    private const string FileName = "subject_relation.map";

    private readonly Dictionary<int, List<RelatedSubject>> _mapping = new();

    private bool _initialized;

    private string FilePath => Path.Join(archive.BasePath, FileName);

    public async Task GenerateIndex(ZipArchive zipStream, CancellationToken token)
    {
        var entry = zipStream.GetEntry("subject-relations.jsonlines");
        if (entry == null) return;
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(token) is { } line)
        {
            var item = JsonSerializer.Deserialize<RelatedSubjectRaw>(line, Constants.JsonSerializerOptions);
            if (item == null) continue;
            if (!_mapping.ContainsKey(item.SubjectId))
                _mapping.Add(item.SubjectId, []);
            _mapping[item.SubjectId].Add(item);
        }

        await Save();
    }

    public async Task<bool> Ready()
    {
        await Load();
        return _mapping.Count > 0;
    }

    public async Task<IEnumerable<Model.RelatedSubject>> Get(int subjectId, CancellationToken token = default)
    {
        await Load();
        if (!_mapping.TryGetValue(subjectId, out var rawList)) return [];

        var relatedSubjects = new List<Model.RelatedSubject>();
        foreach (var relatedSubject in rawList)
        {
            relatedSubjects.Add(await relatedSubject.ToRelatedSubject(archive, token));
        }

        return relatedSubjects;
    }

    private async Task Load()
    {
        if (_initialized) return;
        _initialized = true;

        if (!File.Exists(FilePath)) return;
        await using var fileStream = File.OpenRead(FilePath);
        using var reader = new BinaryReader(fileStream);
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var subjectId = reader.ReadInt32();
            var relatedSubjectId = reader.ReadInt32();
            var relationType = reader.ReadInt16();

            if (!_mapping.ContainsKey(subjectId))
                _mapping.Add(subjectId, []);

            _mapping[subjectId].Add(new RelatedSubject
            {
                RelatedSubjectId = relatedSubjectId,
                RelationType = relationType
            });
        }
    }

    private async Task Save()
    {
        var tempFileName = Path.GetRandomFileName();
        var tempFilePath = Path.Join(archive.TempPath, tempFileName);
        await using var outStream = File.OpenWrite(tempFilePath);
        await using var writer = new BinaryWriter(outStream);
        foreach (var (subjectId, relatedSubjects) in _mapping)
        foreach (var relatedSubject in relatedSubjects)
        {
            writer.Write(subjectId);
            writer.Write(relatedSubject.RelatedSubjectId);
            writer.Write(relatedSubject.RelationType);
        }

        writer.Flush();
        await outStream.FlushAsync();
        outStream.Close();

        if (File.Exists(FilePath))
            File.Move(FilePath, Path.Join(archive.TempPath, Path.GetRandomFileName()), true);
        File.Move(tempFilePath, FilePath, true);
    }
}
