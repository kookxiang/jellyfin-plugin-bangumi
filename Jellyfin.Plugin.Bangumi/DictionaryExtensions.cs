using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi
{
    public static class DictionaryExtensions
    {
        public static T GetOrDefault<TKey, T>(this IDictionary<TKey, T> dict, TKey key)
        {
            T value;
            if (dict.TryGetValue(key, out value))
                return value;

            return default;
        }
    }
}