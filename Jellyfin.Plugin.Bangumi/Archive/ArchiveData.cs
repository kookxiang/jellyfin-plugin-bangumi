using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Bangumi.Archive.Data;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Bangumi.Archive;

public class ArchiveData(IApplicationPaths paths)
{
    public readonly string BasePath = Path.Join(paths.DataPath, "bangumi", "archive");

    public readonly string TempPath = Path.Join(paths.DataPath, "bangumi", "archive", "temp");

    public List<IArchiveStore> Stores =>
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
}