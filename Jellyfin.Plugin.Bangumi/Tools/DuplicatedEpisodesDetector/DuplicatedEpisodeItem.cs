using System;

namespace Jellyfin.Plugin.Bangumi.Tools.DuplicatedEpisodesDetector;

// ReSharper disable PropertyCanBeMadeInitOnly.Global
public class DuplicatedEpisodeItem
{
    public Guid Id { get; set; }

    public Guid SeriesId { get; set; }

    public string? SeriesName { get; set; }

    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public string? Name { get; set; }

    public string Path { get; set; } = null!;

    public DateTime LastModified { get; set; }

    public long? Ticks { get; set; }
}
