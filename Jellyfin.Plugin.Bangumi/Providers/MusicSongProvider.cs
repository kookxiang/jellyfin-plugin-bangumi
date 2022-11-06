using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Providers;

public class MusicSongProvider : IRemoteMetadataProvider<Audio, SongInfo>, IHasOrder
{
    private readonly BangumiApi _api;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MusicSongProvider> _log;
    private readonly Plugin _plugin;

    public MusicSongProvider(BangumiApi api, Plugin plugin, ILibraryManager libraryManager, ILogger<MusicSongProvider> log)
    {
        _api = api;
        _plugin = plugin;
        _libraryManager = libraryManager;
        _log = log;
    }

    public int Order => -5;

    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Audio>> GetMetadata(SongInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var episode = await GetSong(info, token);

        _log.LogInformation("metadata for {FilePath}: {EpisodeInfo}", Path.GetFileName(info.Path), episode);

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

        result.Item.Name = episode.GetName(_plugin.Configuration);
        result.Item.OriginalTitle = episode.OriginalName;
        result.Item.IndexNumber = (int)episode.Order;
        result.Item.ParentIndexNumber = episode.Disc;
        result.Item.Overview = string.IsNullOrEmpty(episode.Description) ? null : episode.Description;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SongInfo searchInfo, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }

    private async Task<Episode?> GetSong(ItemLookupInfo info, CancellationToken token)
    {
        var fileName = Path.GetFileName(info.Path);
        if (string.IsNullOrEmpty(fileName))
            return null;

        var album = _libraryManager.FindByPath(info.Path, false).FindParent<MusicAlbum>();
        if (album is null)
            return null;

        var albumId = album.ProviderIds.GetValueOrDefault(Constants.ProviderName);
        if (string.IsNullOrEmpty(albumId))
            return null;

        double songIndex = info.IndexNumber ?? 0;

        var songId = info.ProviderIds?.GetValueOrDefault(Constants.ProviderName);
        if (!string.IsNullOrEmpty(songId))
        {
            var song = await _api.GetEpisode(songId, token);
            if (song == null)
                goto NoBangumiId;

            if (_plugin.Configuration.TrustExistedBangumiId)
                return song;

            if ($"{song.ParentId}" == albumId && Math.Abs(song.Order - songIndex) < 0.1)
                return song;
        }

        NoBangumiId:
        var episodeListData = await _api.GetSubjectEpisodeList(albumId, null, songIndex, token);
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