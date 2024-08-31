using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Jellyfin.Plugin.Bangumi.Model;

public class InfoBox : Dictionary<string, string>
{
    private const string StartPattern = "{{Infobox";
    private const string EndPattern = "}}";

    public static InfoBox ParseJson(JsonElement data)
    {
        var infobox = new InfoBox();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            if (!item.TryGetProperty("key", out var key))
                continue;
            if (!item.TryGetProperty("value", out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
                infobox.Add(key.GetString() ?? "", value.GetString() ?? "");

            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var subItem in value.EnumerateArray())
                {
                    if (subItem.ValueKind != JsonValueKind.Object) continue;
                    if (!subItem.TryGetProperty("k", out var subKey))
                        continue;
                    if (!subItem.TryGetProperty("v", out var subValue))
                        continue;
                    infobox.Add((key.GetString() ?? "") + "/" + (subKey.GetString() ?? ""), subValue.GetString() ?? "");
                }
        }

        return infobox;
    }

    public static InfoBox ParseString(string data)
    {
        var infobox = new InfoBox();
        var reader = new StringReader(data.ReplaceLineEndings("\n"));
        var line = reader.ReadLine();
        if (line?.StartsWith(StartPattern) != true)
            throw new FormatException("text not begin with defined pattern");
        while ((line = reader.ReadLine()) != EndPattern)
        {
            if (line?.StartsWith("|") != true)
                continue;
            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;
            parts[0] = parts[0][1..];
            if (parts[1] == "{")
                while ((line = reader.ReadLine()) != "}" && line != null)
                {
                    if (!line.StartsWith("[") || !line.EndsWith("]"))
                        continue;
                    line = line.Substring(1, line.Length - 2);
                    var subParts = line.Split('|', 2);
                    if (subParts.Length == 2)
                        infobox.Add(parts[0] + "/" + subParts[0], subParts[1].Trim());
                    else if (infobox.ContainsKey(parts[0]))
                        infobox[parts[0]] += "\n" + line.Trim();
                    else
                        infobox.Add(parts[0], line.Trim());
                }
            else
                infobox.Add(parts[0], parts[1].Trim());
        }

        return infobox;
    }

    public string? Get(string key)
    {
        return TryGetValue(key, out var value) ? value : null;
    }

    public string[]? GetList(string key)
    {
        return TryGetValue(key, out var value) ? value.Split("\n") : null;
    }
}