using System.Collections.Generic;

namespace Jellyfin.Plugin.Bangumi;

public static class DictionaryExtensions
{
    public static T? GetOrDefault<TKey, T>(this IDictionary<TKey, T> dict, TKey key)
    {
        return dict.TryGetValue(key, out var value) ? value : default;
    }
}

public static class StringExtensions
{
    private const string MarkdownForcedLineBreak = "<br>\n";

    public static string ToMarkdown(this string input)
    {
        var content = input.ReplaceLineEndings(MarkdownForcedLineBreak);

        while (content.Contains(MarkdownForcedLineBreak + MarkdownForcedLineBreak + MarkdownForcedLineBreak))
            content = content.Replace(
                MarkdownForcedLineBreak + MarkdownForcedLineBreak + MarkdownForcedLineBreak,
                MarkdownForcedLineBreak + MarkdownForcedLineBreak
            );

        content = content.Replace(MarkdownForcedLineBreak + MarkdownForcedLineBreak, "\n\n");

        return content;
    }
}
