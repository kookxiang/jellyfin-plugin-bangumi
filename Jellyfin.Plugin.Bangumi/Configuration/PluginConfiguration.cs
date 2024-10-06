using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Bangumi.Configuration;

public enum TranslationPreferenceType
{
    Original,
    Chinese
}

public class PluginConfiguration : BasePluginConfiguration
{
    public TranslationPreferenceType TranslationPreference { get; set; } = TranslationPreferenceType.Chinese;

    public TranslationPreferenceType PersonTranslationPreference { get; set; } = TranslationPreferenceType.Original;

    public int RequestTimeout { get; set; } = 5000;

    public string BaseServerUrl { get; set; } = "https://api.bgm.tv";

    public bool ReportPlaybackStatusToBangumi { get; set; } = true;

    public bool SkipNSFWPlaybackReport { get; set; } = true;

    public bool ReportManualStatusChangeToBangumi { get; set; } = false;

    public bool TrustExistedBangumiId { get; set; } = false;

    public bool UseBangumiSeasonTitle { get; set; } = true;

    public bool AlwaysReplaceEpisodeNumber { get; set; }

    public bool AlwaysGetTitleByAnitomySharp { get; set; }

    public bool AlwaysGetEpisodeByAnitomySharp { get; set; }

    public bool UseTestingSearchApi { get; set; }

    public int SeasonGuessMaxSearchCount { get; set; } = 2;
}