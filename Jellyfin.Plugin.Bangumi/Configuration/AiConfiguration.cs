using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace Jellyfin.Plugin.Bangumi.Configuration;

public class AiConfiguration
{
    public List<AiProviderConfiguration> Providers { get; set; } = [];

    public AiFeatureConfiguration SummaryTranslation { get; set; } = new()
    {
        Prompt = "请将以下作品简介翻译为简体中文。\n作品原名：{{original_title}}\n已知译名：{{localized_title}}\n仅翻译原文，不增加剧情，不附加解释，保留原有段落。\n\n原文：\n{{source_text}}"
    };

    public AiFeatureConfiguration FallbackTitle { get; set; } = new()
    {
        Prompt = "请根据以下文件名生成简短、可读的简体中文标题。\n作品原名：{{original_series_title}}\n已知译名：{{localized_series_title}}\n内容类型提示：{{content_kind_hint}}\n忽略字幕组、编码和分辨率，保留片头、片尾编号等有效信息。不要编造内容，仅返回标题。\n\n文件名：\n{{file_name}}"
    };
}

public class AiProviderConfiguration
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Format { get; set; } = "OpenAI";
    public AiPricingConfiguration Pricing { get; set; } = new();
}

/// <summary>Optional USD prices per million tokens. Null means unpriced; zero means free.</summary>
public class AiPricingConfiguration
{
    public decimal? Input { get; set; }
    public decimal? Output { get; set; }
    public decimal? CachedInput { get; set; }
    public decimal? CacheCreation { get; set; }
}

public class AiFeatureConfiguration
{
    private string _prompt = "";

    public bool Enabled { get; set; }
    public string ProviderId { get; set; } = "";

    // Read the retired XML field solely to preserve existing prompt language on upgrade.
    [XmlElement("TargetLanguage")]
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LegacyTargetLanguage { get; set; }
    public bool ShouldSerializeLegacyTargetLanguage() => false;

    public string Prompt
    {
        get => Regex.Replace(_prompt, @"\{\{\s*target_language\s*\}\}", _ => LegacyTargetLanguage is { Length: > 0 } ? LegacyTargetLanguage : "简体中文");
        set => _prompt = value ?? "";
    }
}
