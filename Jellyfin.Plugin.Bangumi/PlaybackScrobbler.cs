using System;
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
using Microsoft.Extensions.Hosting;
using CollectionType = Jellyfin.Plugin.Bangumi.Model.CollectionType;

namespace Jellyfin.Plugin.Bangumi;

public class PlaybackScrobbler(IUserDataManager userDataManager, OAuthStore store, BangumiApi api, Logger<PlaybackScrobbler> log)
    : IHostedService
{
    private static PluginConfiguration Configuration => Plugin.Instance!.Configuration;

    public Task StopAsync(CancellationToken token)
    {
        userDataManager.UserDataSaved -= OnUserDataSaved;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken token)
    {
        userDataManager.UserDataSaved += OnUserDataSaved;
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
            log.Info("item {Name} (#{Id}) doesn't have bangumi id, ignored", item.Name, item.Id);
            return;
        }

        if (!int.TryParse(item.GetParent()?.GetProviderId(Constants.ProviderName), out var subjectId))
            log.Warn("parent of item {Name} (#{Id}) doesn't have bangumi subject id", item.Name, item.Id);

        if (!localConfiguration.Report)
        {
            log.Info("playback report is disabled via local configuration");
            return;
        }

        if (item is Audio)
        {
            log.Info("audio playback report is not supported by bgm.tv, ignored");
            return;
        }

        if (item is Movie)
        {
            subjectId = (subjectId == 0) ? episodeId : subjectId;
            // jellyfin only have subject id for movie, so we need to get episode id from bangumi api
            var episodeList = await api.GetSubjectEpisodeListWithOffset(subjectId, EpisodeType.Normal, 0, CancellationToken.None);
            if (episodeList?.Data.Count > 0)
                episodeId = episodeList.Data.First().Id;
        }

        store.Load();
        var user = store.Get(userId);
        if (user == null)
        {
            log.Info("access token for user #{User} not found, ignored", userId);
            return;
        }

        if (user.Expired)
        {
            log.Info("access token for user #{User} expired, ignored", userId);
            return;
        }

        try
        {
            if (item is Book)
            {
                log.Info("report subject #{Subject} status {Status} to bangumi", episodeId, CollectionType.Watched);
                await api.UpdateCollectionStatus(user.AccessToken, episodeId, played ? CollectionType.Watched : CollectionType.Watching,
                    CancellationToken.None);
            }
            else
            {
                if (subjectId == 0)
                {
                    var episode = await api.GetEpisode(episodeId, CancellationToken.None);
                    if (episode != null)
                        subjectId = episode.ParentId;
                }

                var subject = await api.GetSubject(subjectId, CancellationToken.None);
                if (subject?.IsNSFW == true && Configuration.SkipNSFWPlaybackReport)
                {
                    log.Info("item #{Name} marked as NSFW, skipped", item.Name);
                    return;
                }

                var episodeStatus = await api.GetEpisodeStatus(user.AccessToken, episodeId, CancellationToken.None);
                if (episodeStatus?.Type == EpisodeCollectionType.Watched)
                {
                    log.Info("item {Name} (#{Id}) has been marked as watched before, ignored", item.Name,
                        item.Id);
                    return;
                }

                log.Info("report episode #{Episode} status {Status} to bangumi", episodeId,
                    played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default);
                await api.UpdateEpisodeStatus(user.AccessToken, episodeId,
                    played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default, CancellationToken.None);
            }

            log.Info("report completed");
        }
        catch (Exception e)
        {
            if (played && e.Message == "Bad Request: you need to add subject to your collection first")
            {
                log.Info("report subject #{Subject} status {Status} to bangumi", subjectId, CollectionType.Watching);
                await api.UpdateCollectionStatus(user.AccessToken, subjectId, CollectionType.Watching, CancellationToken.None);

                log.Info("report episode #{Episode} status {Status} to bangumi", episodeId, EpisodeCollectionType.Watched);
                await api.UpdateEpisodeStatus(user.AccessToken, episodeId,
                    played ? EpisodeCollectionType.Watched : EpisodeCollectionType.Default, CancellationToken.None);
            }
            else
            {
                log.Error("report playback status failed: {Error}", e);
            }
        }

        // report subject status watched
        if (played && item is not Book)
        {
            // skip if episode type not normal
            var episode = await api.GetEpisode(episodeId, CancellationToken.None);
            if (episode is { Type: EpisodeType.Normal })
            {
                // check each episode status
                var epList = await api.GetEpisodeCollectionInfo(user.AccessToken, subjectId, (int)EpisodeType.Normal,
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
                        log.Info("report subject #{Subject} status {Status} to bangumi", subjectId, CollectionType.Watched);
                        await api.UpdateCollectionStatus(user.AccessToken, subjectId, CollectionType.Watched, CancellationToken.None);
                    }
                }
            }
        }
    }
}