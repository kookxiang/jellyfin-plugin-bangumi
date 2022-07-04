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
        AlwaysUseAnitomySharp = false;
    }

    public TranslationPreferenceType TranslationPreference { get; set; }

    public bool AlwaysReplaceEpisodeNumber { get; set; }

    public bool AlwaysUseAnitomySharp { get; set; }
}