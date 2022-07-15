namespace Jellyfin.Plugin.Bangumi.Utils;

public class BangumiHelper
{
    public static string NameHelper(string searchName, Plugin plugin)
    {
        if (plugin.Configuration.AlwaysGetTitleByAnitomySharp) searchName = AnitomyHelper.ExtractAnimeTitle(searchName);

        return searchName;
    }
}