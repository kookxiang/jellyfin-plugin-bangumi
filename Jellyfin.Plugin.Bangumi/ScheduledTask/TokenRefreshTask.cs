#if EMBY
using MediaBrowser.Model.Logging;
#else
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Entities;
#endif
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class TokenRefreshTask(IActivityManager activity, BangumiApi api, OAuthStore store)
    : IScheduledTask
{
    public string Key => "OAuthTokenRefreshTask";
    public string Name => "OAuth 登录令牌刷新";
    public string Description => "OAuth 授权令牌到期前自动刷新";
    public string Category => "Bangumi";


    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromDays(1).Ticks,
                MaxRuntimeTicks = TimeSpan.FromMinutes(10).Ticks
            }
        };
    }

#if EMBY
    public Task Execute(CancellationToken token, IProgress<double> progress)
    {
        var task = Task.Run(async () => await ExecuteAsync(progress, token));
        task.Wait();
        return Task.CompletedTask;
    }
#endif

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
    {
        store.Load();
        var users = store.GetUsers();
        var current = 0d;
        var total = users.Count;
        foreach (var (guid, user) in users)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(current / total);
            current++;
            if (user.Expired)
                continue;

#if EMBY
            var activityLogEntry = new ActivityLogEntry
            {
                Name = "Bangumi 授权",
                Type = "Bangumi",
            };
            try
            {
                await user.Refresh(api.GetHttpClient(), token);
                await user.GetProfile(api, token);
                activityLogEntry.ShortOverview = $"用户 #{user.UserId} 授权刷新成功";
                activityLogEntry.Severity = LogSeverity.Info;
            }
            catch (Exception e)
            {
                activityLogEntry.ShortOverview = $"用户 #{user.UserId} 授权刷新失败: {e.Message}";
                activityLogEntry.Severity = LogSeverity.Warn;
            }

            activity.Create(activityLogEntry);
#else
            var userId = Guid.Parse(guid);
            var activityLog = new ActivityLog("Bangumi 授权", "Bangumi", userId);
            try
            {
                await user.Refresh(api.GetHttpClient(), token);
                await user.GetProfile(api, token);
                activityLog.ShortOverview = $"用户 #{user.UserId} 授权刷新成功";
                activityLog.LogSeverity = LogLevel.Information;
            }
            catch (Exception e)
            {
                activityLog.ShortOverview = $"用户 #{user.UserId} 授权刷新失败: {e.Message}";
                activityLog.LogSeverity = LogLevel.Warning;
            }

            await activity.CreateAsync(activityLog);
#endif
        }

        store.Save();
    }
}