using MediaBrowser.Model.Logging;

namespace Jellyfin.Plugin.Bangumi;

public class Logger<T>(ILogger logger)
{
    public void Info(string message, params object?[] args)
    {
        logger.Info(message, args);
    }

    public void Warn(string message, params object?[] args)
    {
        logger.Warn(message, args);
    }

    public void Error(string message, params object?[] args)
    {
        logger.Error(message, args);
    }
}
