#if EMBY
using MediaBrowser.Model.Logging;
#else
using Microsoft.Extensions.Logging;
#endif
/// <summary>
/// https://github.com/DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult
/// </summary>
namespace Jellyfin.Plugin.Bangumi.Utils;
internal static class Logger
{
        private static ILogger Log { get; } = Plugin.Log;

        public static void Info(string text)
        {
#if EMBY
                Log?.Info(text);
#else
                Log?.LogInformation(text);
#endif
        }

        public static void Error(string text)
        {
#if EMBY
                Log?.Error(text);
#else
                Log?.LogError(text);
#endif
        }

        public static void Debug(string text)
        {
#if EMBY
                Log?.Debug(text);
#else
                Log?.LogDebug(text);
#endif
        }

        public static void Warning(string text)
        {
#if EMBY
                Log?.Warn(text);
#else
                Log?.LogWarning(text);
#endif
        }
}
