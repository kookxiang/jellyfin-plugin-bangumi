import '../components/select.ts';
import '../components/checkbox-group.ts';
import { detailsLink } from './details-link.ts';
import { ToolController, registerTool, action, checkbox } from './tool.ts';
interface MissingItem {
    Id: string;
    Name: string;
    Path: string;
    SeriesId?: string;
    SeasonId?: string;
    SeasonNumber?: number;
    SeriesName?: string;
    SeasonName?: string;
    LibraryName: string;
    BangumiProviderEnabled: boolean;
}
interface RefreshResult {
    QueuedCount: number;
    FailedCount: number;
    SkippedCount: number;
    ProviderDisabledCount: number;
    QueuedItemIds: string[];
}
export class MissingId extends ToolController {
    mount() {
        this.render(`<p>查找缺失 ID 的电影和剧集，按勾选项重新获取元数据。未启用 Bangumi 提供程序的项目无法刷新。</p>
            <div class="tool-field"><label for="library">媒体库</label><bangumi-select><select id="library"><option value="">全部媒体库</option></select></bangumi-select></div>
            <div class="actions">${action('扫描视频', 'scan')}${action('刷新所选元数据', 'refresh', 'secondary')}</div><div id="results"></div>`);
        const library = this.query<HTMLSelectElement>('#library');
        library.onchange = () => {
            this.query('#results').replaceChildren();
            this.status('已切换媒体库，请重新扫描。');
        };
        void this.run(async () => {
            const libraries = await this.request<{ Id: string; Name: string }[]>('MissingBangumiId/Libraries');
            for (const item of libraries) library.append(new Option(item.Name || '未命名媒体库', item.Id));
            this.status('选择媒体库后开始扫描。');
        });
        this.query('#scan').onclick = () => this.run(() => this.scan());
        this.query('#refresh').onclick = () =>
            this.run(async () => {
                const selected = Array.from(
                    this.root.querySelectorAll<HTMLInputElement>('input:checked:not([data-unavailable])'),
                );
                if (!selected.length) {
                    this.status('请先勾选需要刷新的视频。');
                    return;
                }
                if (!(await this.confirm(`将所选 ${selected.length} 个视频加入元数据刷新队列？`))) {
                    this.status('已取消刷新。');
                    return;
                }
                if (!this.element.isConnected) return;
                const result = await this.request<RefreshResult>('MissingBangumiId/Refresh', {
                    items: selected.map((input) => input.value).join(','),
                });
                const queued = new Set(result.QueuedItemIds.map((id) => id.toLowerCase()));
                for (const input of selected)
                    if (queued.has(input.value.toLowerCase())) {
                        input.checked = false;
                        input.dataset.unavailable = 'true';
                        input.closest('.result-row')!.querySelector('small')!.textContent += ' · 已排队';
                    }
                this.status(
                    `已排队 ${result.QueuedCount} 项，失败 ${result.FailedCount} 项，跳过 ${result.SkippedCount} 项，提供程序未启用 ${result.ProviderDisabledCount} 项。`,
                );
            }).then(() => this.updateDisabled());
    }
    private updateDisabled() {
        this.root
            .querySelectorAll<HTMLInputElement>('input[data-unavailable]')
            .forEach((input) => (input.disabled = true));
    }
    private async scan() {
        const libraryId = this.query<HTMLSelectElement>('#library').value;
        const items = await this.request<MissingItem[]>(
            'MissingBangumiId/Items' + (libraryId ? '?libraryId=' + encodeURIComponent(libraryId) : ''),
        );
        const results = this.query('#results');
        results.replaceChildren();
        const seriesGroups = new Map<
            string,
            { section: HTMLElement; seasons: Map<string, HTMLElement>; first: MissingItem }
        >();
        for (const item of items) {
            const seriesKey = item.SeriesId || (item.SeriesName ? `${item.LibraryName}/${item.SeriesName}` : item.Id);
            let group = seriesGroups.get(seriesKey);
            if (!group) {
                const section = document.createElement('section');
                section.className = 'missing-series';
                const header = document.createElement('header');
                header.className = 'result-group-header';
                const heading = document.createElement('h3');
                heading.textContent = item.SeriesName || item.Name || '未命名视频';
                header.append(heading);
                const targetId = item.SeriesId || (!item.SeriesName ? item.Id : null);
                if (targetId) header.append(detailsLink(targetId, heading.textContent, 'open_in_new'));
                section.append(header);
                results.append(section);
                group = { section, seasons: new Map(), first: item };
                seriesGroups.set(seriesKey, group);
            }
            const seasonKey = item.SeasonId || String(item.SeasonNumber ?? item.SeasonName ?? 'none');
            let files = group.seasons.get(seasonKey);
            if (!files) {
                const season = document.createElement('section');
                season.className = 'missing-season';
                const seasonTitle =
                    item.SeasonName || (item.SeasonNumber != null ? `第 ${item.SeasonNumber} 季` : '未分季');
                files = document.createElement('bangumi-checkbox-group');
                files.setAttribute('aria-label', `${item.SeriesName || item.Name} · ${seasonTitle}`);
                season.append(files);
                group.section.append(season);
                group.seasons.set(seasonKey, files);
            }
            const row = document.createElement('div');

            row.innerHTML = checkbox(`missing-${item.Id}`, '');
            const fileCheckbox = row.querySelector('bangumi-checkbox')!;
            fileCheckbox.className = 'result-row';
            const input = row.querySelector('input')!;
            input.value = item.Id;
            if (!item.BangumiProviderEnabled) {
                input.dataset.unavailable = 'true';
                input.disabled = true;
            }
            const label = row.querySelector('label')!;
            const filename = item.Path?.split(/[\\/]/).pop() || item.Name;
            label.textContent = filename;
            label.title = item.Path || '';
            const meta = document.createElement('small');
            meta.textContent = `${item.Path || '无文件路径'}${item.BangumiProviderEnabled ? '' : ' · 所属媒体库未启用 Bangumi'}`;
            label.append(meta);
            const link = detailsLink(item.Id, filename);
            link.slot = 'actions';
            fileCheckbox.append(link);
            files.append(fileCheckbox);
        }
        for (const { section, seasons, first } of seriesGroups.values()) {
            if (seasons.size !== 1 || (!first.SeriesName && !first.SeriesId)) continue;
            const header = section.querySelector('.result-group-header')!;
            const heading = header.querySelector('h3')!;
            heading.textContent =
                first.SeasonName ||
                (first.SeasonNumber != null ? `第 ${first.SeasonNumber} 季` : first.SeriesName) ||
                first.Name;
            header.querySelector('a')?.remove();
            const target = first.SeasonId || first.SeriesId;
            if (target) header.append(detailsLink(target, heading.textContent, 'open_in_new'));
        }
        this.status(items.length ? `发现 ${items.length} 个缺失 ID 的视频。` : '没有缺失 Bangumi ID 的视频。');
    }
}
registerTool('bangumi-tool-missing-id', MissingId);
