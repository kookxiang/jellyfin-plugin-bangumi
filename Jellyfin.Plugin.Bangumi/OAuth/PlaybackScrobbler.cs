using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.Model;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Bangumi.OAuth
{
    public class PlaybackScrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager;
        private readonly IUserManager _userManager;
        private readonly OAuthStore _store;
        private readonly BangumiApi _api;

        private Dictionary<Guid, HashSet<Guid>> _ignoreList = new();

        public PlaybackScrobbler(ISessionManager sessionManager, IUserManager userManager, OAuthStore store, BangumiApi api)
        {
            _sessionManager = sessionManager;
            _userManager = userManager;
            _store = store;
            _api = api;
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
            if (e.Item.IsPlayed(user))
                _ignoreList[e.Session.UserId].Add(e.Item.Id);
        }

        private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            var episodeId = e.MediaInfo.GetProviderId(Constants.ProviderName);

            // only process files with bangumi id
            if (string.IsNullOrEmpty(episodeId))
                return;

            // complete episode or finish 80% of the episode
            if (!e.PlayedToCompletion || e.PlaybackPositionTicks > e.MediaInfo.RunTimeTicks * 0.8)
                return;

            var user = _store.Get(e.Session.UserId);
            // access token is required
            if (user == null)
                return;

            // abort if access token is expired
            if (user.Expired)
                return;

            // ignore if the episode is already played
            if (_ignoreList.ContainsKey(e.Session.UserId) && _ignoreList[e.Session.UserId].Contains(e.Item.Id))
                return;

            // update episode status to bangumi
            _api.UpdateEpisodeStatus(user.AccessToken, episodeId, EpisodeStatus.Watched, CancellationToken.None)
                .ConfigureAwait(false);

            // add episode to ignore list
            if (_ignoreList.ContainsKey(e.Session.UserId))
                _ignoreList[e.Session.UserId].Add(e.Item.Id);
        }
    }
}