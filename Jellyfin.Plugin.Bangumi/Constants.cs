using System.Text.Json;

namespace Jellyfin.Plugin.Bangumi;

public static class Constants
{
    public const string ProviderName = "Bangumi";

    public const string SeasonNumberProviderName = "Bangumi Season Number";

    public const string PluginName = "Bangumi";

    public const string PluginGuid = "41b59f1b-a6cf-474a-b416-785379cbd856";

    public const string Language = "zh";

    public static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
