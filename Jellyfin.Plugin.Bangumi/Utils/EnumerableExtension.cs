using System;
using System.Collections.Generic;
using System.Linq;
/// <summary>
/// https://github.com/DirtyRacer1337/Jellyfin.Plugin.PhoenixAdult
/// </summary>
internal static class EnumerableExtension
{
    public static IEnumerable<(int index, T item)> WithIndex<T>(this IEnumerable<T> source)
        => source.Select((item, index) => (index, item));

    public static T Random<T>(this IEnumerable<T> enumerable)
    {
        var r = new Random();
        var list = enumerable as IList<T> ?? enumerable.ToList();

        return list.ElementAt(r.Next(0, list.Count));
    }
}