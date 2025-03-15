using System.Threading.Tasks;

namespace Jellyfin.Plugin.Bangumi.Parser;

public interface IEpisodeParser
{
    Task<Model.Episode?> GetEpisode();
}