using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bangumi.OAuth;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Bangumi.ScheduledTask;

public class TokenRefreshTask(BangumiApi api, OAuthStore store, Logger<TokenRefreshTask> logger)
    : IScheduledTask
{
    public string Key => "OAuthTokenRefreshTask";
    public string Name => "OAuth 登录令牌刷新";
    public string Description => "OAuth 授权令牌到期前自动刷新";
    public string Category => "Bangumi";


    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
#if EMBY
                Type = TaskTriggerInfo.TriggerInterval,
#else
                Type = TaskTriggerInfoType.IntervalTrigger,
#endif
                IntervalTicks = TimeSpan.FromDays(1).Ticks,
                MaxRuntimeTicks = TimeSpan.FromMinutes(10).Ticks
            }
        ];
    }

#if EMBY
    public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        return ExecuteAsync(progress, cancellationToken);
    }
#endif

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        store.Load();
        var users = store.GetUsers();
        var current = 0d;
        var total = users.Count;

#if EMBY
        var httpClient = api.GetHttpClient();
#else
        using var httpClient = api.GetHttpClient();
#endif

        foreach (var (guid, user) in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(current / total);
            current++;
            if (user.Expired || string.IsNullOrEmpty(user.RefreshToken))
            {
                logger.Info("用户 #{user.UserId} 未授权或授权已过期");
                continue;
            }

            try
            {
                await user.Refresh(httpClient, cancellationToken);
                await user.GetProfile(api, cancellationToken);
                logger.Info($"用户 #{user.UserId} 授权刷新成功");
            }
            catch (Exception e)
            {
                logger.Error($"用户 #{user.UserId} 授权刷新失败", e);
            }
        }

        store.Save();
    }
}
