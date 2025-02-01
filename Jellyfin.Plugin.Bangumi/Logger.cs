using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Bangumi;

[SuppressMessage("Usage", "CA2254: Template should be a static expression")]
public class Logger<T>(ILogger<T> logger)
{
    public void Info(string message, params object?[] args)
    {
        logger.LogInformation(message, args);
    }

    public void Warn(string message, params object?[] args)
    {
        logger.LogWarning(message, args);
    }

    public void Error(string message, params object?[] args)
    {
        logger.LogError(message, args);
    }
}
