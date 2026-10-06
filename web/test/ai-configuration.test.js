import test from 'node:test';
import assert from 'node:assert/strict';
import { normalizeConfiguration, validatePrompt, features, providerUsage } from '../src/ai/configuration.ts';

test('old configurations start with AI features disabled and keep independent editable defaults', () => {
    const config = normalizeConfiguration();
    assert.deepEqual(config.Providers, []);
    for (const feature of features) {
        assert.equal(config[feature.key].Enabled, false);
        assert.equal(validatePrompt(config[feature.key].Prompt, feature.variables, feature.requiredVariable), '');
    }
    config.SummaryTranslation.Prompt = 'custom';
    assert.notEqual(normalizeConfiguration().SummaryTranslation.Prompt, 'custom');
});

test('normalization preserves saved providers, disabled feature choices and additional settings', () => {
    const saved = {
        Providers: [
            {
                Id: 'stable-id',
                Name: '<script>',
                Format: 'Responses',
                Model: 'test',
                ApiKey: '',
                Endpoint: 'http://localhost/v1',
            },
        ],
        SummaryTranslation: { Enabled: false, ProviderId: 'stable-id', Prompt: '{{source_text}}', FutureOption: true },
        FutureFeature: { Enabled: false },
    };
    const result = normalizeConfiguration(saved);
    assert.equal(result.SummaryTranslation.ProviderId, 'stable-id');
    assert.equal(result.SummaryTranslation.FutureOption, true);
    assert.deepEqual(result.FutureFeature, saved.FutureFeature);
    result.Providers[0].Name = 'renamed';
    assert.equal(saved.Providers[0].Name, '<script>');
    assert.deepEqual(providerUsage(result, 'stable-id'), ['简介翻译']);
});

test('prompt validation reports typos and missing source inputs before sending a request', () => {
    const feature = features[0];
    const check = (prompt) => validatePrompt(prompt, feature.variables, feature.requiredVariable);
    assert.match(check('{{source_text}} {{target_langauge}}'), /未知变量.*target_langauge/);
    assert.match(check('Translate to English'), /需要包含.*source_text/);
    assert.match(check('Translate {{ source_text }} to {{target_language}}'), /未知变量.*target_language/);
    assert.equal(check('Translate {{ source_text }} to English'), '');
});

test('retired language settings migrate into custom prompts without resetting other feature settings', () => {
    const saved = {
        SummaryTranslation: {
            Enabled: true,
            ProviderId: 'stable-id',
            TargetLanguage: '日本語',
            Prompt: 'Translate {{source_text}} to {{ target_language }}. Reply only in {{target_language}}.',
            FutureOption: true,
        },
        FallbackTitle: { Enabled: false, Prompt: '生成 {{target_language}} 标题：{{file_name}}' },
    };
    const result = normalizeConfiguration(saved);
    assert.equal(result.SummaryTranslation.Prompt, 'Translate {{source_text}} to 日本語. Reply only in 日本語.');
    assert.equal(result.SummaryTranslation.Enabled, true);
    assert.equal(result.SummaryTranslation.ProviderId, 'stable-id');
    assert.equal(result.SummaryTranslation.FutureOption, true);
    assert.equal(result.FallbackTitle.Prompt, '生成 简体中文 标题：{{file_name}}');
    assert.equal('TargetLanguage' in result.SummaryTranslation, false);
    assert.match(saved.SummaryTranslation.Prompt, /target_language/);
    for (const feature of features)
        assert.equal(validatePrompt(result[feature.key].Prompt, feature.variables, feature.requiredVariable), '');
});
