using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Bangumi.Archive.Data;
using Jellyfin.Plugin.Bangumi.Archive.Relation;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi.Archive;

public class ArchiveData(IApplicationPaths paths)
{
    internal readonly string BasePath = Path.Join(paths.DataPath, "bangumi", "archive");

    internal readonly string TempPath = Path.Join(paths.DataPath, "bangumi", "archive", "temp");

    private string VersionPath => Path.Join(BasePath, "version.json");

    public async Task<bool> IsCurrent(ArchiveVersion version, CancellationToken token = default)
    {
        if (!Stores.All(store => store.Exists()) || !SubjectCharacterRelation.Exists() ||
            !SubjectRelations.Exists() || !SubjectEpisodeRelation.Exists() || !SubjectPersonRelation.Exists())
            return false;
        try
        {
            await using var stream = File.OpenRead(VersionPath);
            return await JsonSerializer.DeserializeAsync<ArchiveVersion>(stream, cancellationToken: token) == version;
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void InvalidateVersion()
    {
        if (!File.Exists(VersionPath)) return;
        Directory.CreateDirectory(TempPath);
        File.Move(VersionPath, Path.Join(TempPath, Path.GetRandomFileName()));
    }

    public async Task SaveVersion(ArchiveVersion version, CancellationToken token = default)
    {
        Directory.CreateDirectory(BasePath);
        var tempPath = Path.Join(BasePath, Path.GetRandomFileName());
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, version, cancellationToken: token);
            await stream.FlushAsync(token);
        }
        token.ThrowIfCancellationRequested();
        File.Move(tempPath, VersionPath, true);
    }

    public ICollection<IArchiveStore> Stores =>
    [
        Character,
        Subject,
        Episode,
        Person
    ];

    public ArchiveStore<Character> Character => new(BasePath, "character.jsonlines");

    public ArchiveStore<Subject> Subject => new(BasePath, "subject.jsonlines");

    public ArchiveStore<Episode> Episode => new(BasePath, "episode.jsonlines");

    public ArchiveStore<Person> Person => new(BasePath, "person.jsonlines");

    public SubjectCharacterRelation SubjectCharacterRelation => new(this);

    public SubjectRelations SubjectRelations => new(this);

    public SubjectEpisodeRelation SubjectEpisodeRelation => new(this);

    public SubjectPersonRelation SubjectPersonRelation => new(this);
}
