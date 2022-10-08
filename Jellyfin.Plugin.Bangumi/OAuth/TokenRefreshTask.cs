using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Notifications;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.OAuth;

public class TokenRefreshTask : IScheduledTask
{
    private readonly IActivityManager _activity;
    private readonly BangumiApi _api;
    private readonly PluginDatabase _db;
    private readonly INotificationManager _notification;
    private readonly Plugin _plugin;

    public TokenRefreshTask(IActivityManager activity, INotificationManager notification, Plugin plugin, BangumiApi api, PluginDatabase db)
    {
        _activity = activity;
        _notification = notification;
        _plugin = plugin;
        _api = api;
        _db = db;
    }

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

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken token)
    {
        var users = _db.Logins.FindAll();
        var current = 0d;
        var total = _db.Logins.Count();
        foreach (var user in users)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(current / total);
            current++;
            if (user.Expired)
                continue;

            var activity = new ActivityLog("Bangumi 授权", "Bangumi", user.Id);
            try
            {
                await user.Refresh(_plugin.GetHttpClient(), user.Id, token);
                await user.GetProfile(_api, token);
                activity.ShortOverview = $"用户 #{user.UserId} 授权刷新成功";
                activity.LogSeverity = LogLevel.Information;
            }
            catch (Exception e)
            {
                activity.ShortOverview = $"用户 #{user.UserId} 授权刷新失败: {e.Message}";
                activity.LogSeverity = LogLevel.Warning;
                await _notification.SendNotification(new NotificationRequest
                {
                    Name = activity.ShortOverview,
                    Description = e.StackTrace,
                    Level = NotificationLevel.Warning,
                    UserIds = new[] { user.Id },
                    Date = DateTime.Now
                }, token);
            }

            await _activity.CreateAsync(activity);
        }
    }
}