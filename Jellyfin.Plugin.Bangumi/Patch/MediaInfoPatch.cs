using HarmonyLib;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Bangumi.Patch;

[HarmonyPatch("MediaBrowser.MediaEncoding.Probing.ProbeResultNormalizer", "GetMediaInfo")]
public class MediaInfoPatch
{
    internal static void Postfix(bool isAudio, string path, MediaInfo __result)
    {
        if (isAudio) return;
        if (__result.RunTimeTicks is not > 0) return;
        if (Plugin.Instance!.MediaTicks.TryGetValue(path, out var ticks) && ticks == __result.RunTimeTicks.Value)
            return;
        if (ticks == 0)
            Plugin.Instance.MediaTicks.Add(path, __result.RunTimeTicks.Value);
        else
            Plugin.Instance.MediaTicks[path] = __result.RunTimeTicks.Value;
        Plugin.Instance.SaveCache();
    }
}