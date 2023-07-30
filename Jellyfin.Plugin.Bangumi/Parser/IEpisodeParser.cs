using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Parser
{
    public interface IEpisodeParser
    {
        Task<Model.Episode?> GetEpisode(int seriesId, double? episodeIndex);
        double? GetEpisodeIndex(string fileName, double? episodeIndex);
    }
}