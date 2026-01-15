using System;

namespace Jellyfin.Plugin.Bangumi.Tools.DuplicatedEpisodesDetector;

// ReSharper disable PropertyCanBeMadeInitOnly.Global
public class DuplicatedEpisodeItem
{
    public Guid Id { get; set; }

    public string Path { get; set; } = null!;

    public DateTime LastModified { get; set; }

    public long? Ticks { get; set; }
}
