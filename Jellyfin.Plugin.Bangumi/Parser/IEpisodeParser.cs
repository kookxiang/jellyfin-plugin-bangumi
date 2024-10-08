using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Parser;

public interface IEpisodeParser
{
    Task<Model.Episode?> GetEpisode();

    Task<object?> GetEpisodeProperty(EpisodeProperty episodeProperty);

}

public enum EpisodeProperty
{
    Id,
    ParentId,
    Type,
    Name,
    OriginalName,
    OriginalNameRaw,
    ChineseName,
    ChineseNameRaw,
    Order,
    Disc,
    Index,
    AirDate,
    Duration,
    Description,
    DescriptionRaw
}