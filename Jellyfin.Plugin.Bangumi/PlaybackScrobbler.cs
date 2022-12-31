using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using CollectionType = Jellyfin.Plugin.Bangumi.Model.CollectionType;

namespace Jellyfin.Plugin.Bangumi;

public class PlaybackScrobbler : IServerEntryPoint
{
    private static readonly Dictionary<Guid, HashSet<string>> Store = new();
    private readonly BangumiApi _api;
    private readonly ILogger<PlaybackScrobbler> _log;

    private readonly Plugin _plugin;
    private readonly OAuthStore _store;
    private readonly IUserDataManager _userDataManager;

    public PlaybackScrobbler(Plugin plugin, IUserManager userManager, IUserDataManager userDataManager, OAuthStore store, BangumiApi api, ILogger<PlaybackScrobbler> log)
    {
        _plugin = plugin;
        _userDataManager = userDataManager;
        _store = store;
        _api = api;
        _log = log;

        foreach (var userId in userManager.UsersIds)
            GetPlaybackHistory(userId);
    }

    public void Dispose()
    {
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        GC.SuppressFinalize(this);
    }

    public Task RunAsync()
    {
        _userDataManager.UserDataSaved += OnUserDataSaved;
        return Task.CompletedTask;
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        switch (e.SaveReason)
        {
            case UserDataSaveReason.TogglePlayed when e.UserData.Played:
                // delay 3 seconds to avoid conflict with playback finished event
                Task.Delay(TimeSpan.FromSeconds(3))
                    .ContinueWith(_ =>
                    {
                        GetPlaybackHistory(e.UserId).Add(e.UserData.Key);
                        _log.LogInformation("mark {Name} (#{Id}) as played for user #{User}", e.Item.Name, e.Item.Id, e.UserId);
                    }).ConfigureAwait(false);
                if (_plugin.Configuration.ReportManualStatusChangeToBangumi)
                    ReportPlaybackStatus(e.Item, e.UserId, true).ConfigureAwait(false);
                break;

            case UserDataSaveReason.TogglePlayed when !e.UserData.Played:
                GetPlaybackHistory(e.UserId).Remove(e.UserData.Key);
                _log.LogInformation("mark {Name} (#{Id}) as new for user #{User}", e.Item.Name, e.Item.Id, e.UserId);
                if (_plugin.Configuration.ReportManualStatusChangeToBangumi)
                    ReportPlaybackStatus(e.Item, e.UserId, true).ConfigureAwait(false);
                break;

            case UserDataSaveReason.PlaybackFinished when e.UserData.Played:
                if (_plugin.Configuration.ReportPlaybackStatusToBangumi)
                    ReportPlaybackStatus(e.Item, e.UserId, true).ConfigureAwait(false);
                e.Keys.ForEach(key => GetPlaybackHistory(e.UserId).Add(key));
                break;
        }
    }

    private async Task ReportPlaybackStatus(BaseItem item, Guid userId, bool played)
    {
        if (!int.TryParse(item.GetProviderId(Constants.ProviderName), out var episodeId))
        {
            _log.LogInformation("item {Name} (#{Id}) doesn't have bangumi id, ignored", item.Name, item.Id);
            return;
        }

        if (!int.TryParse(item.GetParent()?.GetProviderId(Constants.ProviderName), out var subjectId))
            _log.LogWarning("parent of item {Name} (#{Id}) doesn't have bangumi subject id", item.Name, item.Id);

        if (item is Audio)
        {
            _log.LogInformation("audio playback report is not supported by bgm.tv, ignored");
            return;
        }

        if (item is Movie)
        {
            subjectId = episodeId;
            // jellyfin only have subject id for movie, so we need to get episode id from bangumi api
            var episodeList = await _api.GetSubjectEpisodeListWithOffset(episodeId, EpisodeType.Normal, 0, CancellationToken.None);
            if (episodeList?.Data.Count > 0)
                episodeId = episodeList.Data.First().Id;
        }

        var user = _store.Get(userId);
        if (user == null)
        {
            _log.LogInformation("access token for user #{User} not found, ignored", userId);
            return;
        }

        if (user.Expired)
        {
            _log.LogInformation("access token for user #{User} expired, ignored", userId);
            return;
        }

        if (item.GetUserDataKeys().Intersect(GetPlaybackHistory(userId)).Any())
        {
            _log.LogInformation("item {Name} (#{Id}) has been played before, ignored", item.Name, item.Id);
            return;
        }

        try
        {
            if (item is Book)
            {
                _log.LogInformation("report subject #{Subject} status {Status} to bangumi", episodeId, CollectionType.Watched);
                await _api.UpdateCollectionStatus(user.AccessToken, episodeId, played ? CollectionType.Watched : CollectionType.Watching, CancellationToken.None);
            }
            else
            {
                if (subjectId == 0)
                {
                    var episode = await _api.GetEpisode(episodeId, CancellationToken.None);
                    if (episode != null)
                        subjectId = episode.ParentId;
                }

                _log.LogInformation("report episode #{Episode} status {Status} to bangumi", episodeId, EpisodeCollectionType.Watched);
                await _api.UpdateEpisodeStatus(user.AccessToken, subjectId, episodeId, played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default, CancellationToken.None);
            }

            _log.LogInformation("report completed");
        }
        catch (Exception e)
        {
            _log.LogError(e, "report playback status failed");
        }
    }

    private HashSet<string> GetPlaybackHistory(Guid userId)
    {
        if (!Store.TryGetValue(userId, out var history))
            Store[userId] = history = _userDataManager.GetAllUserData(userId).Where(item => item.Played).Select(item => item.Key).ToHashSet();
        return history;
    }
}