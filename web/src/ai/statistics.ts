import type { ApiClient } from '../types.ts';

interface ModelStatistics {
    ProviderId: string;
    ProviderName: string;
    Model: string;
    Calls: number;
    UnknownUsageCalls: number;
    UnpricedCalls: number;
    PricedCalls: number;
    InputTokens: number;
    OutputTokens: number;
    CachedInputTokens: number;
    CacheCreationTokens: number;
    EstimatedCost: number;
    LastUsedAt: string;
}

const counts = ['Calls', 'InputTokens', 'OutputTokens', 'CachedInputTokens', 'CacheCreationTokens'] as const;
const numbers = [...counts, 'UnknownUsageCalls', 'UnpricedCalls', 'PricedCalls', 'EstimatedCost'] as const;
const get = (object: Record<string, any>, key: string) => object[key] ?? object[key[0].toLowerCase() + key.slice(1)];
const integer = (value: number) => value.toLocaleString('zh-CN');
const money = (value: number) =>
    value > 0 && value < 0.000001
        ? '< $0.000001'
        : '$' +
          value.toLocaleString('en-US', {
              minimumFractionDigits: value > 0 && value < 0.0001 ? 6 : 4,
              maximumFractionDigits: 6,
          });
const date = (value: string) => (value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—');

function cost(row: Pick<ModelStatistics, 'Calls' | 'PricedCalls' | 'UnpricedCalls' | 'EstimatedCost'>): string {
    if (!row.Calls) return money(0);
    if (!row.PricedCalls) return '未估算';
    return money(row.EstimatedCost) + (row.UnpricedCalls ? '（部分）' : '');
}

export function createAiStatistics(root: HTMLElement, api: ApiClient) {
    const panel = root.querySelector<HTMLElement>('#ai-statistics-panel');
    const feedback = panel.querySelector<HTMLElement>('[data-statistics-feedback]');
    const confirmation = panel.querySelector<HTMLElement>('.ai-clear-confirm');
    let generation = 0;
    let busy = false;

    function render(data: Record<string, any>) {
        const rows: ModelStatistics[] = (get(data, 'Rows') || []).map((saved) => {
            const row = {} as ModelStatistics;
            for (const key of numbers) row[key] = get(saved, key) || 0;
            for (const key of ['ProviderId', 'ProviderName', 'Model', 'LastUsedAt'] as const)
                row[key] = get(saved, key) || '';
            return row;
        });
        rows.sort((a, b) => b.EstimatedCost - a.EstimatedCost || b.Calls - a.Calls);
        const totals = Object.fromEntries(
            numbers.map((key) => [key, rows.reduce((sum, row) => sum + row[key], 0)]),
        ) as unknown as ModelStatistics;
        for (const key of counts) panel.querySelector(`[data-total="${key}"]`)?.replaceChildren(integer(totals[key]));
        panel.querySelector('[data-total="EstimatedCost"]').textContent = cost(totals);
        const updated = get(data, 'UpdatedAt');
        panel.querySelector('[data-statistics-updated]').textContent = updated ? '更新于 ' + date(updated) : '尚无记录';
        panel.querySelector<HTMLElement>('[data-statistics-empty]').hidden = rows.length > 0;
        panel.querySelector<HTMLElement>('.ai-statistics-scroll').hidden = rows.length === 0;
        const notes = [];
        if (totals.UnknownUsageCalls)
            notes.push(`${integer(totals.UnknownUsageCalls)} 次调用未返回完整用量，Token 仅包含已知部分。`);
        if (totals.UnpricedCalls)
            notes.push(`${integer(totals.UnpricedCalls)} 次调用无法完整估算费用（价格未配置或用量未知）。`);
        panel.querySelector('[data-statistics-note]').textContent = notes.join(' ');

        const body = panel.querySelector('tbody');
        body.replaceChildren();
        const addRow = (target: Element, row: ModelStatistics, total = false) => {
            const tr = document.createElement('tr');
            const heading = document.createElement('th');
            heading.scope = 'row';
            heading.textContent = total ? '合计' : row.Model;
            if (!total) {
                const provider = document.createElement('small');
                provider.textContent = row.ProviderName || '未命名模型提供方';
                heading.append(provider);
            }
            tr.append(heading);
            for (const key of counts) {
                const cell = document.createElement('td');
                cell.textContent = integer(row[key]);
                tr.append(cell);
            }
            const fee = document.createElement('td');
            fee.className = 'ai-statistics-cost';
            fee.textContent = cost(row);
            tr.append(fee);
            const recent = document.createElement('td');
            recent.textContent = total ? '—' : date(row.LastUsedAt);
            tr.append(recent);
            if (row.UnknownUsageCalls || row.UnpricedCalls)
                tr.title = `${integer(row.UnknownUsageCalls)} 次用量不完整，${integer(row.UnpricedCalls)} 次费用未完整估算`;
            target.append(tr);
        };
        for (const row of rows) addRow(body, row);
        const footer = panel.querySelector('tfoot');
        footer.replaceChildren();
        addRow(footer, totals, true);
    }

    function setBusy(value: boolean) {
        busy = value;
        for (const button of panel.querySelectorAll<HTMLButtonElement>('button')) button.disabled = value;
        panel.setAttribute('aria-busy', String(value));
    }

    async function request(clear = false) {
        if (busy) return;
        const version = ++generation;
        setBusy(true);
        feedback.dataset.state = 'pending';
        feedback.textContent = clear ? '正在清零…' : '正在加载用量统计…';
        try {
            const response = await api.fetch({
                url: api.getUrl('/Plugins/Bangumi/AI/Statistics' + (clear ? '/Clear' : '')),
                type: clear ? 'POST' : 'GET',
            });
            const result = await response.json();
            if (version !== generation) return;
            if (!response.ok || get(result, 'Success') === false)
                throw new Error(get(result, 'Message') || `HTTP ${response.status}`);
            render(get(result, 'Statistics'));
            feedback.dataset.state = 'success';
            feedback.textContent = clear ? '用量统计已清零。' : '';
            if (clear) {
                confirmation.hidden = true;
            }
        } catch (error) {
            if (version !== generation || error.name === 'AbortError') return;
            feedback.dataset.state = 'error';
            feedback.textContent = (clear ? '清零失败：' : '加载失败：') + (error.message || '请求失败');
        } finally {
            if (version === generation) {
                setBusy(false);
                if (clear && confirmation.hidden)
                    panel.querySelector<HTMLButtonElement>('[data-action="ask-clear-statistics"]').focus();
            }
        }
    }

    root.addEventListener('click', (event) => {
        const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button');
        if (!button || button.disabled) return;
        if (button.id === 'ai-statistics-tab' || button.dataset.action === 'refresh-statistics') void request();
        if (button.dataset.action === 'ask-clear-statistics') {
            confirmation.hidden = false;
            confirmation.querySelector<HTMLButtonElement>('[data-action="cancel-clear-statistics"]').focus();
        }
        if (button.dataset.action === 'cancel-clear-statistics') {
            confirmation.hidden = true;
            panel.querySelector<HTMLButtonElement>('[data-action="ask-clear-statistics"]').focus();
        }
        if (button.dataset.action === 'clear-statistics') void request(true);
    });
    return {
        refresh: () => request(),
        hide: () => {
            generation++;
            setBusy(false);
            confirmation.hidden = true;
            feedback.textContent = '';
        },
    };
}
