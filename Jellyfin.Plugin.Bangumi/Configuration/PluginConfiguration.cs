using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Bangumi.Configuration
{
    public enum TranslationPreferenceType
    {
        Original,
        Chinese
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            ActorOnlyInStaff = true;
            TranslationPreference = TranslationPreferenceType.Chinese;
        }

        public TranslationPreferenceType TranslationPreference { get; set; }

        public bool ActorOnlyInStaff { get; set; }

        public bool AlwaysReplaceEpisodeNumber { get; set; }
    }
}