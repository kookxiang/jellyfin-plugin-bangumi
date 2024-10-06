using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bangumi.Model;

public class InfoBox : List<InfoBoxItem>
{
    public string? GetString(string key)
    {
        try
        {
            return this.FirstOrDefault(x => x.Key == key)?.Value.GetString();
        }
        catch
        {
            return null;
        }
    }

    public string[]? GetAliasStrings(string key)
    {
        try
        {
            var element = this.FirstOrDefault(x => x.Key == key);

            if (element?.Value.ValueKind == JsonValueKind.Array)
            {
                return element.Value.EnumerateArray()
                .Select(x => x.GetProperty("v").GetString()!)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}

public class InfoBoxItem
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = null!;

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; } = default!;
}