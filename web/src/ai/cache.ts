import type { ApiClient } from '../types.ts';

export function createAiCache(root: HTMLElement, api: ApiClient) {
    const panel = root.querySelector<HTMLElement>('.ai-cache-settings');
    const mode = panel.querySelector<HTMLSelectElement>('[data-cache-mode]');
    const days = panel.querySelector<HTMLInputElement>('[data-cache-days]');
    const confirmation = panel.querySelector<HTMLElement>('[data-cache-confirm]');
    const feedback = panel.querySelector<HTMLElement>('[data-cache-feedback]');
    const clearButton = panel.querySelector<HTMLButtonElement>('[data-action="ask-clear-cache"]');
    let generation = 0;
    let busy = false;

    function syncDuration() {
        const custom = mode.value === 'custom';
        days.disabled = !custom;
        days.closest<HTMLElement>('.inputContainer').hidden = !custom;
    }
    mode.addEventListener('change', syncDuration);

    function setBusy(value: boolean) {
        busy = value;
        for (const button of panel.querySelectorAll<HTMLButtonElement>('button')) button.disabled = value;
        panel.setAttribute('aria-busy', String(value));
    }

    async function clear() {
        if (busy) return;
        const version = ++generation;
        setBusy(true);
        feedback.dataset.state = 'pending';
        feedback.textContent = '正在清理缓存…';
        try {
            const response = await api.fetch({
                url: api.getUrl('/Plugins/Bangumi/AI/Cache/Clear'),
                type: 'POST',
            });
            const result = await response.json();
            if (version !== generation) return;
            if (!response.ok || (result.Success ?? result.success) === false)
                throw new Error(result.Message || result.message || `HTTP ${response.status}`);
            confirmation.hidden = true;
            feedback.dataset.state = 'success';
            feedback.textContent = '缓存已清理，下次刷新元数据时可重新生成译文。';
        } catch (error) {
            if (version !== generation || error.name === 'AbortError') return;
            feedback.dataset.state = 'error';
            feedback.textContent = '清理失败：' + (error.message || '请求失败');
        } finally {
            setBusy(false);
            if (version === generation && confirmation.hidden) clearButton.focus();
        }
    }

    panel.addEventListener('click', (event) => {
        const button = (event.target as HTMLElement).closest<HTMLButtonElement>('button');
        if (!button || button.disabled) return;
        if (button.dataset.action === 'ask-clear-cache') {
            confirmation.hidden = false;
            panel.querySelector<HTMLButtonElement>('[data-action="cancel-clear-cache"]').focus();
        }
        if (button.dataset.action === 'cancel-clear-cache') {
            confirmation.hidden = true;
            clearButton.focus();
        }
        if (button.dataset.action === 'clear-cache') void clear();
    });

    function hide() {
        generation++;
        confirmation.hidden = true;
        feedback.textContent = '';
    }
    return {
        load: (durationDays: number) => {
            hide();
            mode.value = durationDays > 0 ? 'custom' : '0';
            days.value = String(durationDays || 30);
            mode.closest<import('../components/select.ts').BangumiSelect>('bangumi-select')?.refresh();
            syncDuration();
        },
        collect: () => (mode.value === 'custom' ? Number(days.value) : 0),
        hide,
    };
}
