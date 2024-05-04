using System;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities.Audio;
using Jellyfin.Plugin.Bangumi.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using Jellyfin.Plugin.Bangumi.Model;
using System.Threading;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Logging;
using Jellyfin.Plugin.Bangumi.OAuth;
using System.Linq;
using CollectionType = Jellyfin.Plugin.Bangumi.Model.CollectionType;
using MediaBrowser.Model.Net;

namespace Jellyfin.Plugin.Bangumi;

public class PlaybackScrobbler : IServerEntryPoint
{
    // https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Localization/Ratings/jp.csv
    // https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Localization/Ratings/us.csv
    private const int RatingNSFW = 10;
    private readonly ILocalizationManager _localizationManager;
    private readonly ILogger _log;
    private static readonly Dictionary<long, HashSet<string>> Store = new();
    private readonly BangumiApi _api;
    private readonly OAuthStore _store;
    private readonly IUserDataManager _userDataManager;
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public PlaybackScrobbler(IUserManager userManager, IUserDataManager userDataManager, ILocalizationManager localizationManager, OAuthStore store, BangumiApi api, ILogger log)
    {
        _userDataManager = userDataManager;
        _localizationManager = localizationManager;
        _log = log;
        _store = store;
        _api = api;

        foreach (var userId in userManager.GetUsers(new UserQuery { }).Items)
        {
            GetPlaybackHistory(userId.InternalId);
        }
    }

    public void Dispose()
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        GC.SuppressFinalize(this);
    }

    public void Run()
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        switch (e.SaveReason)
        {
            case UserDataSaveReason.TogglePlayed when e.UserData.Played:
                Task.Delay(TimeSpan.FromSeconds(3))
                    .ContinueWith(_ =>
                    {
                        GetPlaybackHistory(e.User.InternalId).Add(e.UserData.Key);
                        _log.Info($"mark {e.Item.Name} (#{e.Item.Id}) as played for user #{e.User.InternalId}");
                    }).ConfigureAwait(false);
                if (Configuration.ReportManualStatusChangeToBangumi)
                    ReportPlaybackStatus(e.Item, e.User, true).ConfigureAwait(false);
                break;

            case UserDataSaveReason.TogglePlayed when !e.UserData.Played:
                GetPlaybackHistory(e.User.InternalId).Remove(e.UserData.Key);
                _log.Info($"mark {e.Item.Name} (#{e.Item.Id}) as new for user #{e.User.InternalId}");
                if (Configuration.ReportManualStatusChangeToBangumi)
                    ReportPlaybackStatus(e.Item, e.User, false).ConfigureAwait(false);
                break;

            case UserDataSaveReason.PlaybackFinished when e.UserData.Played:
                if (Configuration.ReportPlaybackStatusToBangumi)
                    ReportPlaybackStatus(e.Item, e.User, true).ConfigureAwait(false);
                GetPlaybackHistory(e.User.InternalId).Add(e.UserData.Key);
                break;
        }
    }


    private async Task ReportPlaybackStatus(BaseItem item, MediaBrowser.Controller.Entities.User user, bool played)
    {
        var localConfiguration = await LocalConfiguration.ForPath(item.Path);
        if (!int.TryParse(item.GetProviderId(Constants.ProviderName), out var episodeId))
        {
            _log.Info($"item {item.Name} (#{item.Id}) doesn't have bangumi id, ignored");
            return;
        }

        if (!int.TryParse(item.GetParent()?.GetProviderId(Constants.ProviderName), out var subjectId))
            _log.Warn($"parent of item {item.Name} (#{item.Id}) doesn't have bangumi subject id");

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
            // emby will store the subject id as the sole provider id, and there is no episode id
            subjectId = episodeId;
            var episodeList = await _api.GetSubjectEpisodeListWithOffset(subjectId, EpisodeType.Normal, 0, CancellationToken.None);
            if (episodeList?.Data.Count > 0)
                episodeId = episodeList.Data.First().Id;
        }

        _store.Load();
        var _user = _store.Get(user.Id);
        if (_user == null)
        {
            _log.Info($"access token for user #{user.Id} not found, ignored");
            return;
        }

        if (_user.Expired)
        {
            _log.Info($"access token for user #{user.Id} expired, ignored");
            return;
        }

        if (GetPlaybackHistory(user.InternalId).Contains(item.UserDataKey))
        {
            var episodeStatus = await _api.GetEpisodeStatus(_user.AccessToken, episodeId, CancellationToken.None);
            if (played && episodeStatus is { Type: EpisodeCollectionType.Watched })
            {
                _log.Info($"item {item.Name} (#{item.Id}) has been marked as watched before, ignored");
                return;
            }
        }

        try
        {
            if (item is Book)
            {
                _log.Info($"report subject #{episodeId} status {CollectionType.Watched} to bangumi");
                await _api.UpdateCollectionStatus(_user.AccessToken, episodeId, played ? CollectionType.Watched : CollectionType.Watching, CancellationToken.None);
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
                    _log.Info($"item #{item.Name} has rating {ratingLevel} marked as NSFW, skipped");
                    return;
                }
                var status = played ? CollectionType.Watched : CollectionType.Watching;
                _log.Info($"report episode #{episodeId} status {status} to bangumi");
                await _api.UpdateEpisodeStatus(_user.AccessToken, subjectId, episodeId, played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default, CancellationToken.None);
            }

            _log.Info("report completed");
        }
        catch (Exception e)
        {
            if (played && e.Message == "Bad Request: you need to add subject to your collection first")
            {
                try
                {
                    _log.Info($"report subject #{subjectId} status {CollectionType.Watching} to bangumi");
                    await _api.UpdateCollectionStatus(_user.AccessToken, subjectId, CollectionType.Watching, CancellationToken.None);

                    _log.Info($"report episode #{episodeId} status {EpisodeCollectionType.Watched} to bangumi");
                    await _api.UpdateEpisodeStatus(_user.AccessToken, subjectId, episodeId, played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default, CancellationToken.None);
                }
                catch (Exception e2)
                {
                    _log.Error($"report playback status failed, err: {e2}");
                }
            }
            else
            {
                _log.Error($"report playback status failed, err: {e}");
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
                var epList = await _api.GetEpisodeCollectionInfo(_user.AccessToken, subjectId, (int)EpisodeType.Normal, CancellationToken.None);
                if (epList is { Total: > 0 })
                {
                    var subjectPlayed = true;
                    epList.Data.ForEach(ep =>
                    {
                        if (ep.Type != EpisodeCollectionType.Watched) subjectPlayed = false;
                    });
                    if (subjectPlayed)
                    {
                        _log.Info($"report subject #{subjectId} status {CollectionType.Watched} to bangumi");
                        await _api.UpdateCollectionStatus(_user.AccessToken, subjectId, CollectionType.Watched, CancellationToken.None);
                    }
                }
            }
        }
    }

    private HashSet<string> GetPlaybackHistory(long userId)
    {
        if (!Store.TryGetValue(userId, out var history))
            Store[userId] = history = _userDataManager.GetAllUserData(userId).Where(item => item.Played).Select(item => item.Key).ToHashSet();
        return history;
    }
}