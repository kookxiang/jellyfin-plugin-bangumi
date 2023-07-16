using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi;

public static class Extensions
{
    public static T? GetOrDefault<TKey, T>(this IDictionary<TKey, T> dict, TKey key)
    {
        return dict.TryGetValue(key, out var value) ? value : default;
    }
}

/// <summary>
///     Class providing extension methods for working with paths.
///     From: https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Library/PathExtensions.cs#L10
/// </summary>
public static class PathExtensions
{
    /// <summary>
    ///     Gets the attribute value.
    /// </summary>
    /// <param name="str">The STR.</param>
    /// <param name="attribute">The attrib.</param>
    /// <returns>System.String.</returns>
    /// <exception cref="ArgumentException"><paramref name="str" /> or <paramref name="attribute" /> is empty.</exception>
    public static string? GetAttributeValue(this string? str, string attribute)
    {
        if (string.IsNullOrEmpty(str))
            return null;
        if (str.Length == 0)
            throw new ArgumentException("String can't be empty.", nameof(str));

        if (attribute.Length == 0)
            throw new ArgumentException("String can't be empty.", nameof(attribute));

        var attributeIndex = str.IndexOf(attribute, StringComparison.OrdinalIgnoreCase);

        // Must be at least 3 characters after the attribute =, ], any character.
        var maxIndex = str.Length - attribute.Length - 3;
        while (attributeIndex > -1 && attributeIndex < maxIndex)
        {
            var attributeEnd = attributeIndex + attribute.Length;
            if (attributeIndex > 0
                && str[attributeIndex - 1] == '['
                && (str[attributeEnd] == '=' || str[attributeEnd] == '-'))
            {
                var closingIndex = str[attributeEnd..].IndexOf(']');
                // Must be at least 1 character before the closing bracket.
                if (closingIndex > 1)
                    return str[(attributeEnd + 1)..(attributeEnd + closingIndex)].Trim();
            }

            str = str[attributeEnd..];
            attributeIndex = str.IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }
}