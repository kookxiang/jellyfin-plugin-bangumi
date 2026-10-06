export interface AiProvider {
    Id: string;
    Name: string;
    Endpoint: string;
    Model: string;
    ApiKey: string;
    Format: string;
    Pricing?: AiPricing;
}

export interface AiPricing {
    Input?: number | null;
    Output?: number | null;
    CachedInput?: number | null;
    CacheCreation?: number | null;
}

export const priceFields = [
    ['Input', '输入'],
    ['Output', '输出'],
    ['CachedInput', '缓存读取'],
    ['CacheCreation', '缓存写入'],
] as const;

export interface AiFeature {
    Enabled: boolean;
    ProviderId: string;
    Prompt: string;
}

export interface AiConfiguration {
    Providers: AiProvider[];
    SummaryTranslation: AiFeature;
    FallbackTitle: AiFeature;
}

export const formats = [
    ['OpenAI', 'OpenAI 兼容（Chat Completions）'],
    ['Responses', 'OpenAI Responses'],
    ['Anthropic', 'Anthropic Messages'],
] as const;

export const features = [
    {
        key: 'SummaryTranslation',
        name: '简介翻译',
        description: '将作品简介翻译为 Prompt 中指定的语言。',
        variables: ['source_text', 'original_title', 'localized_title'],
        requiredVariable: 'source_text',
        prompt: '请将以下作品简介翻译为简体中文。\n作品原名：{{original_title}}\n已知译名：{{localized_title}}\n仅翻译原文，不增加剧情，不附加解释，保留原有段落。\n\n原文：\n{{source_text}}',
    },
    {
        key: 'FallbackTitle',
        name: '后备标题生成',
        description: '为没有合适标题的特典、片头、片尾和预告生成可读标题。',
        variables: ['file_name', 'original_series_title', 'localized_series_title', 'content_kind_hint'],
        requiredVariable: 'file_name',
        prompt: '请根据以下文件名生成简短、可读的简体中文标题。\n作品原名：{{original_series_title}}\n已知译名：{{localized_series_title}}\n内容类型提示：{{content_kind_hint}}\n忽略字幕组、编码和分辨率，保留片头、片尾编号等有效信息。不要编造内容，仅返回标题。\n\n文件名：\n{{file_name}}',
    },
] as const;

export function normalizeConfiguration(value?: Partial<AiConfiguration>): AiConfiguration {
    const result = { ...value, Providers: structuredClone(value?.Providers || []) } as AiConfiguration;
    for (const feature of features) {
        const { TargetLanguage, ...saved } = (value?.[feature.key] || {}) as Partial<AiFeature> & {
            TargetLanguage?: string;
        };
        result[feature.key] = {
            Enabled: false,
            ProviderId: '',
            Prompt: feature.prompt,
            ...saved,
        };
        result[feature.key].Prompt = result[feature.key].Prompt.replace(
            /\{\{\s*target_language\s*\}\}/g,
            () => TargetLanguage || '简体中文',
        );
    }
    return result;
}

export function validatePrompt(prompt: string, variables: readonly string[], required: string): string {
    const used = [...prompt.matchAll(/\{\{\s*([\w]+)\s*\}\}/g)].map((match) => match[1]);
    const unknown = used.filter((name) => !variables.includes(name));
    if (unknown.length) return '未知变量：' + [...new Set(unknown)].map((name) => `{{${name}}}`).join('、');
    if (!used.includes(required)) return `Prompt 需要包含 {{${required}}}。`;
    return '';
}

export function providerUsage(configuration: AiConfiguration, id: string): string[] {
    return features.filter((feature) => configuration[feature.key].ProviderId === id).map((feature) => feature.name);
}
