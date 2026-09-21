import { detailsLink } from './details-link.ts';
import '../components/checkbox-group.ts';
import { ToolController, registerTool, action, checkbox } from './tool.ts';
interface EpisodeFile {
    Id: string;
    SeriesId?: string;
    SeriesName?: string;
    SeasonNumber?: number;
    EpisodeNumber?: number;
    Name?: string;
    Path: string;
    LastModified: string;
    Ticks: number | null;
}
interface Duplicate {
    BangumiId: number;
    Title: string;
    Items: EpisodeFile[];
}
export class Duplicates extends ToolController {
    mount() {
        this
            .render(`<p>按 Bangumi 剧集 ID 查找重复文件。每组默认保留一个最新文件，勾选其余文件；时间相同时保留列表中的第一个。删除前请核对路径。</p>
            <bangumi-checkbox-group aria-label="扫描选项">${checkbox('specials', '跳过特典', true)}${checkbox('length', '检查视频时长', true)}</bangumi-checkbox-group>
            <div class="actions">${action('扫描重复剧集', 'scan')}${action('删除所选文件', 'delete', 'danger')}</div><div id="results"></div>`);
        this.query<HTMLButtonElement>('#delete').disabled = true;
        this.query('#scan').onclick = () =>
            this.run(() => this.scan()).then(() => {
                this.query<HTMLButtonElement>('#delete').disabled = !this.root.querySelector('#results input');
            });
        this.query('#delete').onclick = () =>
            this.run(async () => {
                const ids = Array.from(this.root.querySelectorAll<HTMLInputElement>('#results input:checked')).map(
                    (input) => input.value,
                );
                if (!ids.length) {
                    this.status('请先选择需要删除的文件。');
                    return;
                }
                if (!(await this.confirm(`确定永久删除所选的 ${ids.length} 个文件吗？此操作无法撤销。`))) {
                    this.status('已取消删除。');
                    return;
                }
                if (!this.element.isConnected) return;
                await this.request('DuplicatedEpisodesDetector/Delete', { items: ids.join(',') });
                await this.scan();
            });
    }
    private async scan() {
        const groups = await this.request<Duplicate[]>('DuplicatedEpisodesDetector/Scan', {
            length: this.query<HTMLInputElement>('#length').checked,
            specials: !this.query<HTMLInputElement>('#specials').checked,
        });
        const results = this.query('#results');
        results.replaceChildren();
        const seriesSections = new Map<string, HTMLElement>();
        for (const group of groups) {
            // Keep cross-series ID collisions together so no duplicate file is hidden.
            const seriesKey = [...new Set(group.Items.map((file) => file.SeriesId || file.SeriesName || 'unknown'))]
                .sort()
                .join('|');
            let series = seriesSections.get(seriesKey);
            if (!series) {
                series = document.createElement('section');
                series.className = 'duplicate-series';
                const title = document.createElement('h3');
                title.textContent =
                    [...new Set(group.Items.map((file) => file.SeriesName).filter(Boolean))].join(' / ') ||
                    '未命名系列';
                series.append(title);
                seriesSections.set(seriesKey, series);
                results.append(series);
            }
            const section = document.createElement('section');
            const heading = document.createElement('h4');
            heading.textContent = `${group.Title} · Bangumi ${group.BangumiId}`;
            section.className = 'duplicate-episode';
            const bangumiLink = document.createElement('a');
            bangumiLink.href = 'https://bgm.tv/ep/' + encodeURIComponent(group.BangumiId);
            bangumiLink.target = '_blank';
            bangumiLink.rel = 'noopener noreferrer';
            bangumiLink.textContent = heading.textContent;
            heading.replaceChildren(bangumiLink);
            section.append(heading);
            const retainedFile = group.Items.reduce(
                (latest, file) =>
                    new Date(file.LastModified).getTime() > new Date(latest.LastModified).getTime() ? file : latest,
                group.Items[0],
            );
            const files = document.createElement('bangumi-checkbox-group');
            files.setAttribute('aria-label', group.Title);
            section.append(files);
            for (const file of group.Items) {
                const row = document.createElement('div');
                row.innerHTML = checkbox(`file-${file.Id}`, '');
                const fileCheckbox = row.querySelector('bangumi-checkbox')!;
                fileCheckbox.className = 'result-row';
                const input = row.querySelector('input')!;
                input.value = file.Id;
                input.checked = file !== retainedFile;
                const label = row.querySelector('label')!;
                const filename = file.Path.split(/[\\/]/).pop() || file.Path;
                label.textContent = filename;
                label.title = file.Path;
                const meta = document.createElement('small');
                const seconds = Math.floor((file.Ticks || 0) / 10000000);
                meta.textContent = `${new Date(file.LastModified).toLocaleString()} · ${Math.floor(seconds / 60)} 分 ${seconds % 60} 秒`;
                label.append(meta);
                const link = detailsLink(file.Id, filename);
                link.slot = 'actions';
                fileCheckbox.append(link);
                files.append(fileCheckbox);
            }
            series.append(section);
        }
        this.status(groups.length ? `发现 ${groups.length} 组重复剧集，请勾选需要删除的文件。` : '未发现重复剧集。');
        this.query<HTMLButtonElement>('#delete').disabled = !groups.length;
    }
}
registerTool('bangumi-tool-duplicates', Duplicates);
