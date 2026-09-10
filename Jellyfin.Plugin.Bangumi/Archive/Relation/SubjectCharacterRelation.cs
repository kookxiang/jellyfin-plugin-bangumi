using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;

namespace Jellyfin.Plugin.Bangumi.Archive.Relation;

public class SubjectCharacterRelation(ArchiveData archive)
{
    private string FilePath => Path.Join(archive.BasePath, "subject_character.v1.map");

    public async Task GenerateIndex(ZipArchive zip, CancellationToken token)
    {
        var charactersEntry = zip.GetEntry("subject-characters.jsonlines");
        var actorsEntry = zip.GetEntry("person-characters.jsonlines");
        if (charactersEntry == null || actorsEntry == null) return;

        var actors = new Dictionary<(int Subject, int Character), List<int>>();
        await using (var stream = await actorsEntry.OpenAsync(token))
        using (var reader = new StreamReader(stream))
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                var row = JsonSerializer.Deserialize<ActorRow>(line, Constants.JsonSerializerOptions);
                if (row == null) continue;
                var key = (row.SubjectId, row.CharacterId);
                if (!actors.TryGetValue(key, out var ids)) actors[key] = ids = [];
                if (!ids.Contains(row.PersonId)) ids.Add(row.PersonId);
            }
        }

        var rows = new List<CharacterRow>();
        await using (var stream = await charactersEntry.OpenAsync(token))
        using (var reader = new StreamReader(stream))
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                var row = JsonSerializer.Deserialize<CharacterRow>(line, Constants.JsonSerializerOptions);
                if (row != null) rows.Add(row);
            }
        }

        var tempPath = Path.Join(archive.TempPath, Path.GetRandomFileName());
        await using (var output = File.Create(tempPath))
        using (var writer = new BinaryWriter(output))
        {
            foreach (var row in rows.OrderBy(row => row.SubjectId).ThenBy(row => row.Type).ThenBy(row => row.Order))
            {
                token.ThrowIfCancellationRequested();
                writer.Write(row.SubjectId);
                writer.Write(row.CharacterId);
                writer.Write(row.Type);
                var ids = actors.GetValueOrDefault((row.SubjectId, row.CharacterId)) ?? [];
                writer.Write(ids.Count);
                foreach (var id in ids) writer.Write(id);
            }
        }
        File.Move(tempPath, FilePath, true);
    }

    // null requests API fallback for old archives or incomplete records.
    public async Task<IEnumerable<RelatedCharacter>?> Get(int subjectId, CancellationToken token = default)
    {
        if (!File.Exists(FilePath)) return null;
        var result = new List<RelatedCharacter>();
        using var reader = new BinaryReader(File.OpenRead(FilePath));
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            token.ThrowIfCancellationRequested();
            var currentSubject = reader.ReadInt32();
            if (currentSubject > subjectId) break;
            var characterId = reader.ReadInt32();
            var type = reader.ReadInt32();
            var count = reader.ReadInt32();
            if (currentSubject != subjectId)
            {
                reader.BaseStream.Seek((long)count * sizeof(int), SeekOrigin.Current);
                continue;
            }
            var character = await archive.Character.FindById(characterId, token);
            if (character == null || character.Id != characterId) return null;
            var people = new List<Model.Person>();
            for (var i = 0; i < count; i++)
            {
                var personId = reader.ReadInt32();
                var person = await archive.Person.FindById(personId, token);
                if (person == null || person.Id != personId) return null;
                people.Add(new Model.Person
                {
                    Id = person.Id, Name = person.Name, Type = person.Type, Career = person.Career
                });
            }
            result.Add(new RelatedCharacter
            {
                Id = character.Id,
                Name = character.Name,
                Type = character.Role,
                Relation = type switch { 1 => "主角", 2 => "配角", 3 => "客串", _ => "" },
                Actors = people
            });
        }
        return result.Count > 0 ? result : null;
    }

    private sealed class CharacterRow
    {
        [JsonPropertyName("subject_id")]
        public int SubjectId { get; set; }
        [JsonPropertyName("character_id")]
        public int CharacterId { get; set; }
        public int Type { get; set; }
        public int Order { get; set; }
    }

    private sealed class ActorRow
    {
        [JsonPropertyName("subject_id")]
        public int SubjectId { get; set; }
        [JsonPropertyName("character_id")]
        public int CharacterId { get; set; }
        [JsonPropertyName("person_id")]
        public int PersonId { get; set; }
        public string Summary { get; set; } = "";
    }
}
