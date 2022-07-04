using System;

namespace Jellyfin.Plugin.Bangumi.Utils
{
    public class BangumiHelper
    {
        public static String NameHelper(String searchName, Plugin plugin){

            if (plugin.Configuration.AlwaysUseAnitomySharp){
                searchName = AnitomyHelper.ExtractAnimeTitle(searchName);
            }

            return searchName;
        }

    }
}