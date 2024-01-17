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

    public int RequestTimeout { get; set; } = 5000;

    public bool ReportPlaybackStatusToBangumi { get; set; } = true;

    public bool SkipNSFWPlaybackReport { get; set; } = true;

    public bool ReportManualStatusChangeToBangumi { get; set; } = false;

    public bool TrustExistedBangumiId { get; set; } = false;

    public bool UseBangumiSeasonTitle { get; set; } = true;

    public bool AlwaysReplaceEpisodeNumber { get; set; }

    public bool AlwaysGetTitleByAnitomySharp { get; set; }

    public bool AlwaysGetEpisodeByAnitomySharp { get; set; }

    public bool UseTestingSearchApi { get; set; }

    public bool UseOpenaiTitleFallback { get; set; } = false;

    public string OpenaiToken { get; set; } = "";

    public string OpenaiEndpoint { get; set; } = "https://api.openai.com";

    public string OpenaiModel { get; set; } = "gpt-3.5-turbo";

    public string OpenaiPrompt { get; set; } = @"ファイル名にはいくつかの略語があります、この用語集を参照してください。
IV=interview
NC=creditless
その上で、以下のファイル名から、このファイルに最もふさわしいタイトルを当ててください（日本語で、できるだけ短く、説明の必要はありません、直接お答えください、記号は不要です。）
";
}