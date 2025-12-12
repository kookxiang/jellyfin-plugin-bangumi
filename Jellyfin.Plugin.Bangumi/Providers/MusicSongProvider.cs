using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class MusicSongProvider(BangumiApi api, ILibraryManager libraryManager, Logger<MusicSongProvider> log)
    : IRemoteMetadataProvider<Audio, SongInfo>, IHasOrder
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Audio>> GetMetadata(SongInfo info, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var episode = await GetSong(info, cancellationToken);

        log.Info("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);

        var result = new MetadataResult<Audio> { ResultLanguage = Constants.Language };

        if (episode == null)
            return result;

        result.Item = new Audio();
        result.HasMetadata = true;
        result.Item.ProviderIds.Add(Constants.ProviderName, $"{episode.Id}");

        if (DateTime.TryParse(episode.AirDate, out var airDate))
            result.Item.PremiereDate = airDate;
        if (episode.AirDate.Length == 4)
            result.Item.ProductionYear = int.Parse(episode.AirDate);

        result.Item.Name = episode.OriginalName;
        result.Item.IndexNumber = (int)episode.Order;
        result.Item.ParentIndexNumber = episode.Disc;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SongInfo searchInfo, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        using var httpClient = api.GetHttpClient();
        return await httpClient.GetAsync(url, cancellationToken);
    }

    private async Task<Episode?> GetSong(ItemLookupInfo info, CancellationToken token)
    {
        var fileName = Path.GetFileName(info.Path);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var album = libraryManager.FindByPath(info.Path, false)?.FindParent<MusicAlbum>();
        if (album is null)
            return null;

        if (!int.TryParse(album.ProviderIds.GetValueOrDefault(Constants.ProviderName), out var albumId))
            return null;

        double songIndex = info.IndexNumber ?? 0;

        if (int.TryParse(info.ProviderIds?.GetValueOrDefault(Constants.ProviderName), out var songId))
        {
            var song = await api.GetEpisode(songId, token);
            if (song == null)
                goto NoBangumiId;

            if (Configuration.TrustExistedBangumiId)
                return song;

            if (song.ParentId == albumId && Math.Abs(song.Order - songIndex) < 0.1)
                return song;
        }

        NoBangumiId:
        var episodeListData = await api.GetSubjectEpisodeList(albumId, null, songIndex, token);
        if (episodeListData == null)
            return null;
        try
        {
            return episodeListData.OrderBy(x => x.Type).First(x => x.Order.Equals(songIndex));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
