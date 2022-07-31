using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Bangumi.Configuration;

public enum TranslationPreferenceType
{
    Original,
    Chinese
}

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        TranslationPreference = TranslationPreferenceType.Chinese;
        ReportPlaybackStatusToBangumi = true;
        ReportManualStatusChangeToBangumi = false;
        AlwaysGetTitleByAnitomySharp = false;
        AlwaysGetEpisodeByAnitomySharp = false;
    }

    public TranslationPreferenceType TranslationPreference { get; set; }

    public bool ReportPlaybackStatusToBangumi { get; set; }

    public bool ReportManualStatusChangeToBangumi { get; set; }

    public bool AlwaysReplaceEpisodeNumber { get; set; }

    public bool AlwaysGetTitleByAnitomySharp { get; set; }

    public bool AlwaysGetEpisodeByAnitomySharp { get; set; }
}