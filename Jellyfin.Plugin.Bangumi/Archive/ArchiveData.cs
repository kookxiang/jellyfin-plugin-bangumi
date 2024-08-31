using System.Collections.Generic;
using System.IO;
using Jellyfin.Plugin.Bangumi.Archive.Data;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Archive;

public class ArchiveData(IApplicationPaths paths, ILogger<ArchiveData> log)
{
    public readonly string BasePath = Path.Join(paths.DataPath, "bangumi", "archive");

    public readonly string TempPath = Path.Join(paths.DataPath, "bangumi", "archive", "temp");

    public List<IArchiveStore> Stores =>
    [
        Character,
        Person
    ];

    public ArchiveStore<Character> Character => new(BasePath, "character.jsonlines");

    public ArchiveStore<Person> Person => new(BasePath, "person.jsonlines");
}