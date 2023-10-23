using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi.Rule;

public class IgnoreRule : IResolverIgnoreRule
{
    private readonly ILogger<IgnoreRule> _logger;

    public IgnoreRule(ILogger<IgnoreRule> logger)
    {
        _logger = logger;
    }
    
    public bool ShouldIgnore(FileSystemMetadata fileInfo, BaseItem parent)
    {
        if (!Plugin.Instance!.Configuration.IgnoreLessThanFiveMinutes) return false;
        if (!Plugin.Instance.MediaTicks.TryGetValue(fileInfo.FullName, out var ticks))
        {
            _logger.LogDebug($"processing file {fileInfo.FullName} error: unknown ticks");
            return false;
        }

        var span = TimeSpan.FromTicks(ticks);
        if (span.TotalMinutes < 5)
        {
            _logger.LogInformation($"Ignore file {fileInfo.FullName}. because duration {span.TotalMinutes} min < 5 min");
            return true;
        }

        return false;
    }
}