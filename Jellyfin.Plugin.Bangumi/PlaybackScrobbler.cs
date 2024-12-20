﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Configuration;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Hosting;
using CollectionType = Jellyfin.Plugin.Bangumi.Model.CollectionType;

namespace Jellyfin.Plugin.Bangumi;

public class PlaybackScrobbler : IHostedService
{
    // https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Localization/Ratings/jp.csv
    // https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Localization/Ratings/us.csv
    private const int RatingNSFW = 10;

    private readonly BangumiApi _api;
    private readonly ILocalizationManager _localizationManager;
    private readonly Logger<PlaybackScrobbler> _log;

    private readonly OAuthStore _store;
    private readonly IUserDataManager _userDataManager;

    public PlaybackScrobbler(IUserDataManager userDataManager, ILocalizationManager localizationManager, OAuthStore store, BangumiApi api,
        Logger<PlaybackScrobbler> log)
    {
        _userDataManager = userDataManager;
        _localizationManager = localizationManager;
        _store = store;
        _api = api;
        _log = log;
    }

    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public Task StopAsync(CancellationToken token)
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken token)
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        switch (e.SaveReason)
        {
            case UserDataSaveReason.TogglePlayed when e.UserData.Played:
                if (Configuration.ReportManualStatusChangeToBangumi)
                    ReportPlaybackStatus(e.Item, e.UserId, true).ConfigureAwait(false);
                break;

            case UserDataSaveReason.TogglePlayed when !e.UserData.Played:
                if (Configuration.ReportManualStatusChangeToBangumi)
                    ReportPlaybackStatus(e.Item, e.UserId, false).ConfigureAwait(false);
                break;

            case UserDataSaveReason.PlaybackFinished when e.UserData.Played:
                if (Configuration.ReportPlaybackStatusToBangumi)
                    ReportPlaybackStatus(e.Item, e.UserId, true).ConfigureAwait(false);
                break;
        }
    }

    private async Task ReportPlaybackStatus(BaseItem item, Guid userId, bool played)
    {
        var localConfiguration = await LocalConfiguration.ForPath(item.Path);
        if (!int.TryParse(item.GetProviderId(Constants.ProviderName), out var episodeId))
        {
            _log.Info("item {Name} (#{Id}) doesn't have bangumi id, ignored", item.Name, item.Id);
            return;
        }

        if (!int.TryParse(item.GetParent()?.GetProviderId(Constants.ProviderName), out var subjectId))
            _log.Warn("parent of item {Name} (#{Id}) doesn't have bangumi subject id", item.Name, item.Id);

        if (!localConfiguration.Report)
        {
            _log.Info("playback report is disabled via local configuration");
            return;
        }

        if (item is Audio)
        {
            _log.Info("audio playback report is not supported by bgm.tv, ignored");
            return;
        }

        if (item is Movie)
        {
            subjectId = (subjectId == 0) ? episodeId : subjectId;
            // jellyfin only have subject id for movie, so we need to get episode id from bangumi api
            var episodeList = await _api.GetSubjectEpisodeListWithOffset(subjectId, EpisodeType.Normal, 0, CancellationToken.None);
            if (episodeList?.Data.Count > 0)
                episodeId = episodeList.Data.First().Id;
        }

        _store.Load();
        var user = _store.Get(userId);
        if (user == null)
        {
            _log.Info("access token for user #{User} not found, ignored", userId);
            return;
        }

        if (user.Expired)
        {
            _log.Info("access token for user #{User} expired, ignored", userId);
            return;
        }

        try
        {
            if (item is Book)
            {
                _log.Info("report subject #{Subject} status {Status} to bangumi", episodeId, CollectionType.Watched);
                await _api.UpdateCollectionStatus(user.AccessToken, episodeId, played ? CollectionType.Watched : CollectionType.Watching,
                    CancellationToken.None);
            }
            else
            {
                if (subjectId == 0)
                {
                    var episode = await _api.GetEpisode(episodeId, CancellationToken.None);
                    if (episode != null)
                        subjectId = episode.ParentId;
                }

                var ratingLevel = item.OfficialRating is null ? null : _localizationManager.GetRatingLevel(item.OfficialRating);
                if (ratingLevel == null)
                    foreach (var parent in item.GetParents())
                    {
                        if (parent.OfficialRating == null) continue;

                        if (int.TryParse(parent.OfficialRating, out int digitalRating))
                        {
                            // Brazil rating has digital rating level, up to 18 is not NSFW
                            ratingLevel = digitalRating >= 18 ? RatingNSFW : 0;
                            break;
                        }

                        ratingLevel = _localizationManager.GetRatingLevel(parent.OfficialRating);
                        if (ratingLevel != null) break;
                    }

                if (ratingLevel != null && ratingLevel >= RatingNSFW && Configuration.SkipNSFWPlaybackReport)
                {
                    _log.Info("item #{Name} marked as NSFW, skipped", item.Name);
                    return;
                }

                var episodeStatus = await _api.GetEpisodeStatus(user.AccessToken, episodeId, CancellationToken.None);
                if (episodeStatus?.Type == EpisodeCollectionType.Watched)
                {
                    _log.Info("item {Name} (#{Id}) has been marked as watched before, ignored", item.Name,
                        item.Id);
                    return;
                }

                _log.Info("report episode #{Episode} status {Status} to bangumi", episodeId,
                    played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default);
                await _api.UpdateEpisodeStatus(user.AccessToken, subjectId, episodeId,
                    played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default, CancellationToken.None);
            }

            _log.Info("report completed");
        }
        catch (Exception e)
        {
            if (played && e.Message == "Bad Request: you need to add subject to your collection first")
            {
                _log.Info("report subject #{Subject} status {Status} to bangumi", subjectId, CollectionType.Watching);
                await _api.UpdateCollectionStatus(user.AccessToken, subjectId, CollectionType.Watching, CancellationToken.None);

                _log.Info("report episode #{Episode} status {Status} to bangumi", episodeId, EpisodeCollectionType.Watched);
                await _api.UpdateEpisodeStatus(user.AccessToken, subjectId, episodeId,
                    played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default, CancellationToken.None);
            }
            else
            {
                _log.Error("report playback status failed: {Error}", e);
            }
        }

        // report subject status watched
        if (played && item is not Book)
        {
            // skip if episode type not normal
            var episode = await _api.GetEpisode(episodeId, CancellationToken.None);
            if (episode is { Type: EpisodeType.Normal })
            {
                // check each episode status
                var epList = await _api.GetEpisodeCollectionInfo(user.AccessToken, subjectId, (int)EpisodeType.Normal,
                    CancellationToken.None);
                if (epList is { Total: > 0 })
                {
                    var subjectPlayed = true;
                    epList.Data.ForEach(ep =>
                    {
                        if (ep.Type != EpisodeCollectionType.Watched) subjectPlayed = false;
                    });
                    if (subjectPlayed)
                    {
                        _log.Info("report subject #{Subject} status {Status} to bangumi", subjectId, CollectionType.Watched);
                        await _api.UpdateCollectionStatus(user.AccessToken, subjectId, CollectionType.Watched, CancellationToken.None);
                    }
                }
            }
        }
    }
}