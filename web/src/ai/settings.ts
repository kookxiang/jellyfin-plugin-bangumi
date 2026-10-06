import template from './settings.html?raw';
import {
    features,
    formats,
    normalizeConfiguration,
    priceFields,
    providerUsage,
    validatePrompt,
} from './configuration.ts';
import { createAiStatistics } from './statistics.ts';
import type { AiConfiguration, AiFeature, AiProvider } from './configuration.ts';
import type { ApiClient } from '../types.ts';

type Control = HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;

export function createAiSettings(root: HTMLElement, api: ApiClient) {
    root.innerHTML = template;
    const statistics = createAiStatistics(root, api);
    let configuration = normalizeConfiguration();
    let generation = 0;
    const featureList = root.querySelector<HTMLElement>('.ai-feature-list');
    const providerList = root.querySelector<HTMLElement>('.ai-provider-list');
    const field = (card: Element, name: string) => card.querySelector<Control>(`[data-field="${name}"]`);
    const value = (card: Element, name: string) => field(card, name).value;

    function collect(): AiConfiguration {
        const next = { ...configuration };
        next.Providers = [...providerList.children].map((card, index) => ({
            ...configuration.Providers[index],
            Name: value(card, 'Name'),
            Endpoint: value(card, 'Endpoint'),
            Model: value(card, 'Model'),
            ApiKey: value(card, 'ApiKey'),
            Format: value(card, 'Format'),
            Pricing: {
                ...configuration.Providers[index].Pricing,
                ...Object.fromEntries(
                    priceFields.map(([key]) => [
                        key,
                        value(card, 'Price' + key) === '' ? null : Number(value(card, 'Price' + key)),
                    ]),
                ),
            },
        }));
        for (const feature of features) {
            const card = featureList.querySelector<HTMLElement>(`[data-feature="${feature.key}"]`);
            next[feature.key] = {
                ...configuration[feature.key],
                Enabled: card.querySelector<HTMLInputElement>('.ai-switch').checked,
                ProviderId: value(card, 'ProviderId'),
                Prompt: value(card, 'Prompt'),
            };
        }
        return next;
    }

    function setExpanded(card: Element, expanded: boolean) {
        const button = card.querySelector<HTMLButtonElement>('.ai-expand');
        button.setAttribute('aria-expanded', String(expanded));
        card.querySelector<HTMLElement>('.ai-card-body').hidden = !expanded;
    }

    function syncFeature(card: HTMLElement) {
        const enabled = card.querySelector<HTMLInputElement>('.ai-switch').checked;
        card.querySelector<HTMLButtonElement>('.ai-expand').disabled = !enabled;
        card.querySelector<HTMLFieldSetElement>('fieldset').disabled = !enabled;
        if (!enabled) setExpanded(card, false);
        validateFeature(card);
    }

    function validateFeature(card: HTMLElement) {
        const feature = features.find((item) => item.key === card.dataset.feature);
        const provider = field(card, 'ProviderId');
        provider.setCustomValidity(
            provider.value && !configuration.Providers.some((item) => item.Id === provider.value)
                ? '请选择可用的模型提供方。'
                : '',
        );
        const prompt = field(card, 'Prompt');
        const error = validatePrompt(prompt.value, feature.variables, feature.requiredVariable);
        prompt.setCustomValidity(error);
        card.querySelector<HTMLElement>('[data-prompt-error]').textContent = error;
    }

    function refreshProviderChoices() {
        const providers = [...providerList.children].map((card, index) => ({
            Id: configuration.Providers[index].Id,
            Name: value(card, 'Name'),
        }));
        for (const card of featureList.querySelectorAll<HTMLElement>('.ai-card')) {
            const select = field(card, 'ProviderId') as HTMLSelectElement;
            const selected = select.options.length ? select.value : configuration[card.dataset.feature].ProviderId;
            select.replaceChildren(new Option(providers.length ? '请选择模型提供方' : '请先添加模型提供方', ''));
            for (const provider of providers)
                select.add(new Option(provider.Name.trim() || '未命名模型提供方', provider.Id));
            if (selected && !providers.some((provider) => provider.Id === selected))
                select.add(new Option('模型提供方不可用，请重新选择', selected));
            select.value = selected;
            select.closest<import('../components/select.ts').BangumiSelect>('bangumi-select')?.refresh();
            validateFeature(card);
        }
        root.querySelector<HTMLElement>('.ai-empty').hidden = providers.length > 0;
    }

    function renderFeatures() {
        featureList.replaceChildren();
        for (const feature of features) {
            const card = document.createElement('section');
            card.className = 'ai-card';
            card.dataset.feature = feature.key;
            const prefix = `ai-${feature.key}`;
            card.innerHTML = `
                <div class="ai-card-header">
                    <button type="button" class="ai-expand" aria-expanded="false" aria-controls="${prefix}-body">
                        <span class="material-icons ai-chevron" aria-hidden="true">chevron_right</span>
                        <span><span class="ai-card-title" id="${prefix}-title">${feature.name}</span><span class="ai-card-description" id="${prefix}-description">${feature.description}</span></span>
                    </button>
                    <input type="checkbox" role="switch" class="ai-switch" data-config-ignore aria-label="启用${feature.name}" aria-describedby="${prefix}-description" />
                </div>
                <fieldset class="ai-card-body" id="${prefix}-body" aria-labelledby="${prefix}-title" hidden>
                    <div class="selectContainer"><label class="selectLabel" for="${prefix}-provider">模型提供方</label><bangumi-select><select id="${prefix}-provider" data-field="ProviderId" data-config-ignore required></select></bangumi-select></div>
                    <div class="inputContainer">
                        <div class="ai-prompt-heading"><label class="textareaLabel" for="${prefix}-prompt">Prompt</label><bangumi-button variant="quiet"><button type="button" data-action="reset-prompt">恢复默认</button></bangumi-button></div>
                        <textarea id="${prefix}-prompt" data-field="Prompt" data-config-ignore class="textarea-mono" rows="8" required aria-describedby="${prefix}-variables ${prefix}-error"></textarea>
                        <p class="fieldDescription" id="${prefix}-variables">点击变量插入到 Prompt。可选上下文缺失时替换为空文本。</p>
                        <div class="ai-variables"></div>
                        <p class="ai-feedback" data-state="error" data-prompt-error id="${prefix}-error" role="status"></p>
                    </div>
                </fieldset>`;
            const saved: AiFeature = configuration[feature.key];
            card.querySelector<HTMLInputElement>('.ai-switch').checked = saved.Enabled;
            field(card, 'Prompt').value = saved.Prompt;
            for (const variable of feature.variables) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'ai-variable';
                button.dataset.variable = variable;
                button.textContent = `{{${variable}}}`;
                card.querySelector('.ai-variables').append(button);
            }
            featureList.append(card);
            syncFeature(card);
        }
    }

    function renderProviders(expandId?: string) {
        providerList.replaceChildren();
        configuration.Providers.forEach((provider, index) => {
            const card = document.createElement('section');
            card.className = 'ai-card';
            card.dataset.providerIndex = String(index);
            const prefix = `ai-provider-${index}`;
            card.innerHTML = `
                <div class="ai-card-header">
                    <button type="button" class="ai-expand" aria-expanded="false" aria-controls="${prefix}-body"><span class="material-icons ai-chevron" aria-hidden="true">chevron_right</span><span><span class="ai-card-title"></span><span class="ai-card-description"></span></span></button>
                    <bangumi-button variant="quiet" icon><button type="button" data-action="delete-provider" aria-label="删除模型提供方"><span class="material-icons" aria-hidden="true">delete_outline</span></button></bangumi-button>
                </div>
                <fieldset class="ai-card-body" id="${prefix}-body" hidden>
                    <div class="ai-grid">
                        <div class="inputContainer"><label class="inputLabel" for="${prefix}-name">Name</label><input id="${prefix}-name" data-field="Name" data-config-ignore placeholder="例如：本地模型" required /></div>
                        <div class="selectContainer"><label class="selectLabel" for="${prefix}-format">接口格式</label><bangumi-select><select id="${prefix}-format" data-field="Format" data-config-ignore required></select></bangumi-select></div>
                        <div class="inputContainer ai-wide"><label class="inputLabel" for="${prefix}-endpoint">AI Endpoint</label><input id="${prefix}-endpoint" data-field="Endpoint" data-config-ignore type="url" placeholder="https://api.openai.com/v1" required aria-describedby="${prefix}-endpoint-help" /><p class="fieldDescription" id="${prefix}-endpoint-help">填写 API 基础地址，例如 https://example.com/v1；也可填写完整接口地址。</p></div>
                        <div class="inputContainer"><label class="inputLabel" for="${prefix}-model">Model</label><input id="${prefix}-model" data-field="Model" data-config-ignore placeholder="填写服务支持的模型 ID" required /></div>
                        <div class="inputContainer"><label class="inputLabel" for="${prefix}-key">API Key</label><div class="ai-key-row"><input id="${prefix}-key" data-field="ApiKey" data-config-ignore type="password" autocomplete="new-password" spellcheck="false" /><bangumi-button variant="quiet" icon><button type="button" data-action="toggle-key" aria-label="显示 API Key" aria-pressed="false"><span class="material-icons" aria-hidden="true">visibility</span></button></bangumi-button></div></div>
                    </div>
                    <details class="ai-pricing">
                        <summary>价格（可选）</summary>
                        <p class="fieldDescription">美元 / 百万 Token，用于估算费用。留空表示未配置价格，填 0 表示免费。修改价格仅影响之后的调用。</p>
                        <div class="ai-grid">
                            ${priceFields.map(([key, label]) => `<div class="inputContainer"><label class="inputLabel" for="${prefix}-price-${key}">${label}（$/M）</label><input id="${prefix}-price-${key}" data-field="Price${key}" data-config-ignore type="number" min="0" max="1000000000" step="any" placeholder="未配置" /></div>`).join('')}
                        </div>
                    </details>
                    <div class="ai-provider-actions"><p class="fieldDescription">发送“你好，请简短回复。”，使用当前填写的配置，无需先保存。</p><bangumi-button variant="secondary"><button type="button" data-action="test-provider">测试连接</button></bangumi-button></div>
                    <p class="ai-feedback" data-test-result role="status"></p>
                </fieldset>`;
            for (const [format, label] of formats)
                (field(card, 'Format') as HTMLSelectElement).add(new Option(label, format));
            if (!formats.some(([format]) => format === provider.Format))
                (field(card, 'Format') as HTMLSelectElement).add(
                    new Option('未知接口格式，请重新选择', provider.Format),
                );
            for (const key of ['Name', 'Endpoint', 'Model', 'ApiKey', 'Format'])
                field(card, key).value = provider[key] || '';
            for (const [key] of priceFields)
                field(card, 'Price' + key).value = provider.Pricing?.[key]?.toString() ?? '';
            (field(card, 'Endpoint') as HTMLInputElement).pattern = 'https?://.+';
            field(card, 'Endpoint').title = '请填写 HTTP 或 HTTPS 地址';
            field(card, 'Format').closest<import('../components/select.ts').BangumiSelect>('bangumi-select')?.refresh();
            providerList.append(card);
            updateProviderHeader(card);
            setExpanded(card, provider.Id === expandId);
        });
        refreshProviderChoices();
    }

    function updateProviderHeader(card: HTMLElement) {
        card.querySelector('.ai-card-title').textContent = value(card, 'Name').trim() || '未命名模型提供方';
        const label = formats.find(([format]) => format === value(card, 'Format'))?.[1] || '未知接口格式';
        card.querySelector('.ai-card-description').textContent = [label, value(card, 'Model')]
            .filter(Boolean)
            .join(' · ');
        card.querySelector('[data-action="delete-provider"]').setAttribute(
            'aria-label',
            '删除 ' + (value(card, 'Name') || '模型提供方'),
        );
    }

    function notifyChange() {
        root.dispatchEvent(new Event('change', { bubbles: true }));
    }

    async function testProvider(card: HTMLElement) {
        for (const control of card.querySelectorAll<Control>('input, select')) {
            if (!control.reportValidity()) return;
        }
        const provider = collect().Providers[Number(card.dataset.providerIndex)];
        const snapshot = JSON.stringify(provider);
        const version = generation;
        const button = card.querySelector<HTMLButtonElement>('[data-action="test-provider"]');
        const output = card.querySelector<HTMLElement>('[data-test-result]');
        const current = () =>
            version === generation &&
            card.parentElement === providerList &&
            snapshot === JSON.stringify(collect().Providers[Number(card.dataset.providerIndex)]);
        button.disabled = true;
        button.textContent = '测试中…';
        button.setAttribute('aria-busy', 'true');
        output.dataset.state = 'pending';
        output.textContent = '正在请求模型提供方…';
        try {
            const response = await api.fetch({
                url: api.getUrl('/Plugins/Bangumi/AI/Test'),
                type: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(provider),
            });
            const result = await response.json();
            if (!current()) return;
            if (!response.ok || result.Success === false || result.success === false)
                throw new Error(result.Message || result.message || `HTTP ${response.status}`);
            output.dataset.state = 'success';
            output.textContent = `连接成功 · ${result.ElapsedMilliseconds ?? result.elapsedMilliseconds} ms\n${result.Text ?? result.text}`;
        } catch (error) {
            if (!current() || error.name === 'AbortError') return;
            output.dataset.state = 'error';
            output.textContent = '连接失败：' + (error.message || '请求失败');
        } finally {
            button.disabled = false;
            button.textContent = '测试连接';
            button.removeAttribute('aria-busy');
        }
    }

    root.addEventListener('click', (event) => {
        const target = (event.target as HTMLElement).closest<HTMLElement>('button');
        if (!target || target.matches(':disabled')) return;
        const card = target.closest<HTMLElement>('.ai-card');
        if (target.matches('.ai-expand')) setExpanded(card, target.getAttribute('aria-expanded') !== 'true');
        if (target.dataset.action === 'add-provider') {
            configuration = collect();
            const provider: AiProvider = {
                Id: crypto.randomUUID(),
                Name: '新模型提供方',
                Endpoint: 'https://api.openai.com/v1',
                Model: '',
                ApiKey: '',
                Format: 'OpenAI',
            };
            configuration.Providers.push(provider);
            renderProviders(provider.Id);
            field(providerList.lastElementChild, 'Name').focus();
            notifyChange();
        }
        if (target.dataset.action === 'delete-provider') {
            configuration = collect();
            const index = Number(card.dataset.providerIndex);
            const id = configuration.Providers[index].Id;
            const usage = providerUsage(configuration, id);
            configuration.Providers.splice(index, 1);
            for (const feature of features) {
                if (configuration[feature.key].ProviderId !== id) continue;
                configuration[feature.key].ProviderId = '';
                field(featureList.querySelector(`[data-feature="${feature.key}"]`), 'ProviderId').value = '';
            }
            renderProviders();
            root.querySelector('[data-provider-feedback]').textContent = usage.length
                ? `已删除模型提供方，请为${usage.join('、')}重新选择模型提供方。`
                : '已删除模型提供方。';
            root.querySelector<HTMLButtonElement>('[data-action="add-provider"]').focus();
            notifyChange();
        }
        if (target.dataset.action === 'toggle-key') {
            const input = field(card, 'ApiKey') as HTMLInputElement;
            input.type = input.type === 'password' ? 'text' : 'password';
            target.setAttribute('aria-pressed', String(input.type === 'text'));
            target.setAttribute('aria-label', input.type === 'text' ? '隐藏 API Key' : '显示 API Key');
            target.querySelector('.material-icons').textContent =
                input.type === 'text' ? 'visibility_off' : 'visibility';
        }
        if (target.dataset.action === 'test-provider') void testProvider(card);
        if (target.dataset.action === 'reset-prompt' || target.dataset.variable) {
            const prompt = field(card, 'Prompt') as HTMLTextAreaElement;
            if (target.dataset.action === 'reset-prompt')
                prompt.value = features.find((feature) => feature.key === card.dataset.feature).prompt;
            else
                prompt.setRangeText(
                    `{{${target.dataset.variable}}}`,
                    prompt.selectionStart,
                    prompt.selectionEnd,
                    'end',
                );
            validateFeature(card);
            prompt.focus();
            notifyChange();
        }
    });

    function onEdit(event: Event) {
        const target = event.target as Control;
        const card = target.closest<HTMLElement>('.ai-card');
        if (!card) return;
        if (card.dataset.feature) syncFeature(card);
        else {
            updateProviderHeader(card);
            refreshProviderChoices();
            card.querySelector('[data-test-result]').textContent = '';
        }
    }
    root.addEventListener('input', onEdit);
    root.addEventListener('change', onEdit);
    root.addEventListener(
        'invalid',
        (event) => {
            const card = (event.target as HTMLElement).closest('.ai-card');
            if (!card) return;
            root.querySelector<HTMLButtonElement>(
                card.hasAttribute('data-feature') ? '#ai-feature-tab' : '#ai-provider-tab',
            ).click();
            setExpanded(card, true);
            (event.target as HTMLElement).closest<HTMLDetailsElement>('details')?.setAttribute('open', '');
        },
        true,
    );

    function load(value?: AiConfiguration) {
        generation++;
        statistics.hide();
        configuration = normalizeConfiguration(value);
        renderFeatures();
        renderProviders();
        root.querySelector('[data-provider-feedback]').textContent = '';
        if (root.querySelector('#ai-statistics-tab').getAttribute('aria-selected') === 'true')
            void statistics.refresh();
    }
    load();
    return {
        load,
        collect,
        hide: () => {
            generation++;
            statistics.hide();
        },
    };
}
