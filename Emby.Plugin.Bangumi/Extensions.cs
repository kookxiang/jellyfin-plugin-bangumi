using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi;

public static class Extensions
{
    public static T? GetOrDefault<TKey, T>(this IDictionary<TKey, T> dict, TKey key)
    {
        return dict.TryGetValue(key, out var value) ? value : default;
    }
}