using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AnitomySharp;

namespace Jellyfin.Plugin.Bangumi.Utils
{
    public class AnitomyHelper
    {
        public static List<Element> ElementsOutput(string path)
        {
            return new List<Element>(AnitomySharp.AnitomySharp.Parse(path));
        }
        public static String ExtractAnimeTitle(string path)
        {
            var elements = AnitomySharp.AnitomySharp.Parse(path);
            return elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeTitle).Value;
        }
        public static String ExtractEpisodeTitle(string path)
        {
            var elements = AnitomySharp.AnitomySharp.Parse(path);
            return elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeTitle).Value;
        }
        public static String ExtractEpisodeNumber(string path)
        {
            var elements = AnitomySharp.AnitomySharp.Parse(path);
            return elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeNumber).Value;
        }
        public static String ExtractSeasonNumber(string path)
        {
            var elements = AnitomySharp.AnitomySharp.Parse(path);
            return elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeSeason).Value;
        }
    }
}