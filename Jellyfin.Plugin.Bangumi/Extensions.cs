using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

namespace Jellyfin.Plugin.Bangumi;

public static class Extensions
{
    public static T? GetOrDefault<TKey, T>(this IDictionary<TKey, T> dict, TKey key)
    {
        return dict.TryGetValue(key, out var value) ? value : default;
    }

    public static string GetValue(this Enum item)
    {
        var value = item.ToString();
        return item.GetType().GetMember(value)?[0].GetCustomAttribute<EnumMemberAttribute>(false)?.Value ?? value;
    }
}