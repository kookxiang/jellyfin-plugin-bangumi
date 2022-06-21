using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi;

public class PlaybackScrobbler : IServerEntryPoint
{
    private static readonly Dictionary<Guid, HashSet<string>> Store = new();

    private readonly BangumiApi _api;
    private readonly ILogger<PlaybackScrobbler> _log;
    private readonly OAuthStore _store;
    private readonly IUserDataManager _userDataManager;

    public PlaybackScrobbler(IUserManager userManager, IUserDataManager userDataManager, OAuthStore store, BangumiApi api, ILogger<PlaybackScrobbler> log)
    {
        _userDataManager = userDataManager;
        _store = store;
        _api = api;
        _log = log;

        foreach (var userId in userManager.UsersIds) GetPlaybackHistory(userId);
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
                break;

            case UserDataSaveReason.TogglePlayed when !e.UserData.Played:
                GetPlaybackHistory(e.UserId).Remove(e.UserData.Key);
                _log.LogInformation("mark {Name} (#{Id}) as new for user #{User}", e.Item.Name, e.Item.Id, e.UserId);
                break;

            case UserDataSaveReason.PlaybackFinished when e.UserData.Played:
                var bangumiId = e.Item.GetProviderId(Constants.ProviderName);

                if (string.IsNullOrEmpty(bangumiId))
                {
                    _log.LogInformation("item {Name} (#{Id}) doesn't have bangumi id, ignored", e.Item.Name, e.Item.Id);
                    return;
                }

                var user = _store.Get(e.UserId);
                if (user == null)
                {
                    _log.LogInformation("access token for user #{User} not found, ignored", e.UserId);
                    return;
                }

                if (user.Expired)
                {
                    _log.LogInformation("access token for user #{User} expired, ignored", e.UserId);
                    return;
                }

                if (e.Keys.Intersect(GetPlaybackHistory(e.UserId)).Any())
                {
                    _log.LogInformation("item {Name} (#{Id}) has been played before, ignored", e.Item.Name, e.Item.Id);
                    return;
                }

                _log.LogInformation("report episode #{Episode} status {Status} to bangumi", bangumiId, EpisodeStatus.Watched);
                _api.UpdateEpisodeStatus(user.AccessToken, bangumiId, EpisodeStatus.Watched, CancellationToken.None).Wait();

                _log.LogInformation("report completed");
                e.Keys.ForEach(key => GetPlaybackHistory(e.UserId).Add(key));
                break;
        }
    }

    private HashSet<string> GetPlaybackHistory(Guid userId)
    {
        if (!Store.TryGetValue(userId, out var history))
            Store[userId] = history = _userDataManager.GetAllUserData(userId).Where(item => item.Played).Select(item => item.Key).ToHashSet();
        return history;
    }
}