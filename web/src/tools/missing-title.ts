import '../components/select.ts';
import '../components/checkbox-group.ts';
import { detailsLink } from './details-link.ts';
import { ToolController, registerTool, action, checkbox } from './tool.ts';
import { groupMissingTitles, type MissingTitleItem } from './missing-title-state.ts';

interface RefreshResult {
    QueuedCount: number;
    FailedCount: number;
    SkippedCount: number;
    QueuedItemIds: string[];
}

export class MissingTitle extends ToolController {
    mount() {
        this
            .render(`<p>查找已有 Bangumi ID，但标题仍为空或来自文件名的视频，适用于新番标题尚未及时更新的情况。扫描结果默认全选，提交后由 Jellyfin 刷新元数据。</p>
            <div class="tool-field"><label for="library">媒体库</label><bangumi-select><select id="library"><option value="">全部媒体库</option></select></bangumi-select></div>
            <div class="actions">${action('扫描视频', 'scan')}${action('刷新所选元数据', 'refresh', 'secondary')}</div>
            <div id="selection" hidden>${checkbox('select-all', '全选', true)}</div><div id="results"></div>`);
        const library = this.query<HTMLSelectElement>('#library');
        library.onchange = () => {
            this.query('#results').replaceChildren();
            this.syncSelection();
            this.status('已切换媒体库，请重新扫描。');
        };
        this.query<HTMLInputElement>('#select-all').onchange = (event) => {
            const checked = (event.target as HTMLInputElement).checked;
            this.inputs().forEach((input) => (input.checked = checked));
            this.syncSelection();
        };
        this.query('#scan').onclick = () => this.run(() => this.scan()).then(() => this.syncSelection());
        this.query('#refresh').onclick = () => this.run(() => this.refresh()).then(() => this.syncSelection());
        this.syncSelection();
        void this.run(async () => {
            const libraries = await this.request<{ Id: string; Name: string }[]>('MissingTitle/Libraries');
            for (const item of libraries) library.append(new Option(item.Name || '未命名媒体库', item.Id));
            this.status('选择媒体库后开始扫描。锁定标题、跳过的目录和未启用 Bangumi 的视频不会参与刷新。');
        }).then(() => this.syncSelection());
    }

    private inputs() {
        return Array.from(this.root.querySelectorAll<HTMLInputElement>('input[data-item]:not([data-queued])'));
    }

    private syncSelection() {
        const inputs = this.inputs();
        const count = inputs.filter((input) => input.checked).length;
        const all = this.query<HTMLInputElement>('#select-all');
        all.checked = inputs.length > 0 && count === inputs.length;
        all.indeterminate = count > 0 && count < inputs.length;
        all.disabled = !inputs.length;
        this.query('#selection').hidden = !this.root.querySelector('input[data-item]');
        this.query<HTMLButtonElement>('#refresh').disabled = !count;
        this.root.querySelectorAll<HTMLInputElement>('input[data-queued]').forEach((input) => (input.disabled = true));
    }

    private async scan() {
        const libraryId = this.query<HTMLSelectElement>('#library').value;
        const items = await this.request<MissingTitleItem[]>(
            'MissingTitle/Items' + (libraryId ? '?libraryId=' + encodeURIComponent(libraryId) : ''),
        );
        const results = this.query('#results');
        results.replaceChildren();
        for (const group of groupMissingTitles(items)) {
            const section = document.createElement('section');
            section.className = 'missing-series';
            const header = document.createElement('header');
            header.className = 'result-group-header';
            const heading = document.createElement('h3');
            heading.textContent = `${group.title}（${group.items.length}）`;
            header.append(heading);
            if (group.seriesId) header.append(detailsLink(group.seriesId, group.title, 'open_in_new'));
            const files = document.createElement('bangumi-checkbox-group');
            files.setAttribute('aria-label', group.title);
            for (const item of group.items) {
                const wrapper = document.createElement('div');
                wrapper.innerHTML = checkbox(`title-${item.Id}`, '', true);
                const row = wrapper.querySelector('bangumi-checkbox')!;
                row.className = 'result-row';
                const input = wrapper.querySelector('input')!;
                input.value = item.Id;
                input.dataset.item = 'true';
                input.onchange = () => this.syncSelection();
                const label = wrapper.querySelector('label')!;
                label.textContent = item.Name || '（标题为空）';
                const meta = document.createElement('small');
                meta.textContent = `${item.Reason} · Bangumi ${item.BangumiId} · ${item.Path || ''}`;
                label.append(meta);
                const link = detailsLink(item.Id, item.Name || '视频详情');
                link.slot = 'actions';
                row.append(link);
                files.append(row);
            }
            section.append(header, files);
            results.append(section);
        }
        this.status(items.length ? `发现 ${items.length} 个待更新视频，已默认全选。` : '没有发现需要补全标题的视频。');
    }

    private async refresh() {
        const selected = this.inputs().filter((input) => input.checked);
        if (!selected.length) return;
        const result = await this.request<RefreshResult>('MissingTitle/Refresh', {
            items: selected.map((input) => input.value).join(','),
        });
        const queued = new Set(result.QueuedItemIds.map((id) => id.toLowerCase()));
        for (const input of selected) {
            if (!queued.has(input.value.toLowerCase())) continue;
            input.checked = false;
            input.dataset.queued = 'true';
            input.closest('.result-row')!.querySelector('small')!.textContent += ' · 已加入 Jellyfin 刷新队列';
        }
        this.status(
            `已加入 Jellyfin 刷新队列 ${result.QueuedCount} 项，跳过 ${result.SkippedCount} 项，失败 ${result.FailedCount} 项。刷新完成后可重新扫描。`,
        );
    }
}
registerTool('bangumi-tool-missing-title', MissingTitle);
