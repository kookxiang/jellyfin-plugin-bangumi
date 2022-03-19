using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi
{
    public class PlaybackScrobbler : IServerEntryPoint
    {
        private static readonly Dictionary<Guid, HashSet<string>> Store = new();

        private readonly BangumiApi _api;
        private readonly ILogger<PlaybackScrobbler> _log;
        private readonly ISessionManager _sessionManager;
        private readonly OAuthStore _store;
        private readonly IUserDataManager _userDataManager;

        public PlaybackScrobbler(ISessionManager sessionManager, IUserDataManager userDataManager, OAuthStore store, BangumiApi api, ILogger<PlaybackScrobbler> log)
        {
            _sessionManager = sessionManager;
            _userDataManager = userDataManager;
            _store = store;
            _api = api;
            _log = log;
        }

        public void Dispose()
        {
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            GC.SuppressFinalize(this);
        }

        public Task RunAsync()
        {
            _userDataManager.UserDataSaved += OnUserDataSaved;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
        {
            if (e.SaveReason is UserDataSaveReason.PlaybackProgress or UserDataSaveReason.PlaybackFinished)
                return;

            if (e.UserData.Played)
                GetPlaybackHistory(e.UserId).Add(e.UserData.Key);
        }

        private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            var episodeId = e.MediaInfo.GetProviderId(Constants.ProviderName);

            if (string.IsNullOrEmpty(episodeId))
            {
                _log.LogInformation("item {Name} (#{Id}) doesn't have bangumi id, ignored", e.Item.Name, e.Item.Id);
                return;
            }

            if (!e.PlayedToCompletion && e.PlaybackPositionTicks < e.MediaInfo.RunTimeTicks * 0.8)
            {
                _log.LogInformation("item {Name} (#{Id}) haven't finish yet, ignored", e.Item.Name, e.Item.Id);
                return;
            }

            var user = _store.Get(e.Session.UserId);
            if (user == null)
            {
                _log.LogInformation("access token for user #{User} not found, ignored", e.Session.Id);
                return;
            }

            if (user.Expired)
            {
                _log.LogInformation("access token for user #{User} expired, ignored", e.Session.Id);
                return;
            }

            if (e.Item.GetUserDataKeys().Intersect(GetPlaybackHistory(e.Session.UserId)).Any())
            {
                _log.LogInformation("item {Name} (#{Id}) has been played before, ignored", e.Item.Name, e.Item.Id);
                return;
            }

            _log.LogInformation("report episode #{Episode} status {Status} to bangumi", episodeId, EpisodeStatus.Watched);
            _api.UpdateEpisodeStatus(user.AccessToken, episodeId, EpisodeStatus.Watched, CancellationToken.None).Wait();

            _log.LogInformation("report completed");
            e.Item.GetUserDataKeys().ForEach(key => GetPlaybackHistory(e.Session.UserId).Add(key));
        }

        private HashSet<string> GetPlaybackHistory(Guid userId)
        {
            if (!Store.TryGetValue(userId, out var history))
                Store[userId] = history = _userDataManager.GetAllUserData(userId).Where(item => item.Played).Select(item => item.Key).ToHashSet();
            return history;
        }
    }
}