using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.OAuth
{
    public class PlaybackScrobbler : IServerEntryPoint
    {
        private readonly BangumiApi _api;
        private readonly ILogger<PlaybackScrobbler> _log;
        private readonly ISessionManager _sessionManager;
        private readonly OAuthStore _store;
        private readonly IUserManager _userManager;

        private readonly Dictionary<Guid, HashSet<Guid>> _ignoreList = new();

        public PlaybackScrobbler(ISessionManager sessionManager, IUserManager userManager, OAuthStore store, BangumiApi api, ILogger<PlaybackScrobbler> log)
        {
            _sessionManager = sessionManager;
            _userManager = userManager;
            _store = store;
            _api = api;
            _log = log;
        }

        public void Dispose()
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        }

        public Task RunAsync()
        {
            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            if (!_ignoreList.ContainsKey(e.Session.UserId))
                _ignoreList[e.Session.UserId] = new HashSet<Guid>();
            var user = _userManager.GetUserById(e.Session.UserId);
            if (!e.Item.IsPlayed(user))
                return;
            _log.LogInformation("item #{Id} has been played before, add to ignore list", e.Item.Id);
            _ignoreList[e.Session.UserId].Add(e.Item.Id);
        }

        private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            var episodeId = e.MediaInfo.GetProviderId(Constants.ProviderName);

            if (!_ignoreList.ContainsKey(e.Session.UserId))
                _ignoreList[e.Session.UserId] = new HashSet<Guid>();

            if (string.IsNullOrEmpty(episodeId))
            {
                _log.LogInformation("item #{Id} doesn't have bangumi id, ignored", e.Item.Id);
                return;
            }

            if (!e.PlayedToCompletion && e.PlaybackPositionTicks < e.MediaInfo.RunTimeTicks * 0.8)
            {
                _log.LogInformation("item #{Id} haven't finish yet, ignored", e.Item.Id);
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

            if (_ignoreList[e.Session.UserId].Contains(e.Item.Id))
            {
                _log.LogInformation("access token for user #{User} expired, ignored", e.Session.Id);
                return;
            }

            _log.LogInformation("report episode #{Episode} status {Status} to bangumi", episodeId, EpisodeStatus.Watched);
            _api.UpdateEpisodeStatus(user.AccessToken, episodeId, EpisodeStatus.Watched, CancellationToken.None).Wait();

            _log.LogInformation("report completed, add episode to ignore list");
            _ignoreList[e.Session.UserId].Add(e.Item.Id);
        }
    }
}