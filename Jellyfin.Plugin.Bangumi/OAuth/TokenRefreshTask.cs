using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Notifications;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.OAuth
{
    public class TokenRefreshTask : IScheduledTask
    {
        private readonly IActivityManager _activity;
        private readonly INotificationManager _notification;
        private readonly Plugin _plugin;

        private readonly OAuthStore _store;

        public TokenRefreshTask(IActivityManager activity, INotificationManager notification, Plugin plugin, OAuthStore store)
        {
            _activity = activity;
            _notification = notification;
            _plugin = plugin;
            _store = store;
        }

        public string Key => "OAuthTokenRefreshTask";
        public string Name => "OAuth 登录令牌刷新";
        public string Description => "OAuth 授权令牌到期前自动刷新";
        public string Category => "Bangumi";

        public async Task Execute(CancellationToken token, IProgress<double> progress)
        {
            var users = _store.GetUsers();
            var current = 0d;
            var total = users.Count;
            foreach (var (guid, user) in users)
            {
                token.ThrowIfCancellationRequested();
                progress.Report(current / total);
                current++;
                if (user.Expired)
                    continue;
                if (user.ExpireTime > DateTime.Now.AddDays(1))
                    continue;

                var formData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("client_id", OAuthController.ApplicationId),
                    new KeyValuePair<string, string>("client_secret", OAuthController.ApplicationSecret),
                    new KeyValuePair<string, string>("refresh_token", user.RefreshToken)
                }!);
                var response = await _plugin.GetHttpClient().PostAsync("https://bgm.tv/oauth/access_token", formData, token);
                var responseBody = await response.Content.ReadAsStringAsync(token);
                var activity = new ActivityLog("Bangumi 授权", "Bangumi", Guid.Parse(guid));
                if (!response.IsSuccessStatusCode)
                {
                    var error = JsonSerializer.Deserialize<OAuthError>(responseBody)!;
                    activity.ShortOverview = $"用户 #{user.UserId} 授权刷新失败: {error.ErrorDescription}";
                    activity.LogSeverity = LogLevel.Warning;

                    await _notification.SendNotification(new NotificationRequest
                    {
                        Name = activity.ShortOverview,
                        Level = NotificationLevel.Warning,
                        UserIds = new[] { Guid.Parse(guid) },
                        Date = DateTime.Now
                    }, token);
                }
                else
                {
                    var newUser = JsonSerializer.Deserialize<OAuthUser>(responseBody)!;
                    user.AccessToken = newUser.AccessToken;
                    user.RefreshToken = newUser.RefreshToken;
                    user.ExpireTime = newUser.ExpireTime;
                    activity.ShortOverview = $"用户 #{user.UserId} 授权刷新成功";
                    activity.LogSeverity = LogLevel.Information;
                }

                await _activity.CreateAsync(activity);
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(6).Ticks,
                    MaxRuntimeTicks = TimeSpan.FromMinutes(10).Ticks
                },
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerStartup,
                    MaxRuntimeTicks = TimeSpan.FromMinutes(10).Ticks
                }
            };
        }
    }
}