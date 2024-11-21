using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Archive.Data;

namespace Jellyfin.Plugin.Bangumi.Archive.Relation;

public class SubjectEpisodeRelation(ArchiveData archive)
{
    private const string FileName = "subject_episode.map";

    private readonly Dictionary<int, List<int>> _mapping = new();

    private bool _initialized;

    private string FilePath => Path.Join(archive.BasePath, FileName);

    public async Task GenerateIndex(CancellationToken token)
    {
        foreach (var episode in archive.Episode.Enumerate())
        {
            token.ThrowIfCancellationRequested();
            if (!_mapping.ContainsKey(episode.ParentId))
                _mapping[episode.ParentId] = [];
            _mapping[episode.ParentId].Add(episode.Id);
        }

        await Save();
    }

    public async Task<bool> Ready()
    {
        await Load();
        return _mapping.Count > 0;
    }

    public async Task<List<Episode>> GetEpisodes(int subjectId)
    {
        await Load();
        if (!_mapping.TryGetValue(subjectId, out var idList)) return [];
        var list = new List<Episode>();
        foreach (var id in idList)
        {
            var episode = await archive.Episode.FindById(id);
            if (episode != null)
                list.Add(episode);
        }

        return list;
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
            var episodeCount = reader.ReadUInt16();
            var episodeIdList = new List<int>(episodeCount);
            for (var i = 0; i < episodeCount; i++)
                episodeIdList.Add(reader.ReadInt32());
            _mapping[subjectId] = episodeIdList;
        }
    }

    private async Task Save()
    {
        var tempFileName = Path.GetRandomFileName();
        var tempFilePath = Path.Join(archive.TempPath, tempFileName);
        await using var outStream = File.OpenWrite(tempFilePath);
        await using var writer = new BinaryWriter(outStream);
        foreach (var (subjectId, episodeIdList) in _mapping)
        {
            writer.Write(subjectId);
            writer.Write((ushort)episodeIdList.Count);
            foreach (var episodeId in episodeIdList)
                writer.Write(episodeId);
        }

        writer.Flush();
        await outStream.FlushAsync();
        File.Move(tempFilePath, FilePath, true);
    }
}