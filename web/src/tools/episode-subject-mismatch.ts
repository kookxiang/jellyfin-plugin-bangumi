import '../components/select.ts';
import { detailsLink } from './details-link.ts';
import { ToolController, registerTool, action } from './tool.ts';

interface MismatchItem {
    Id: string;
    Name?: string;
    Path?: string;
    SeriesName?: string;
    SeasonId?: string;
    SeasonName?: string;
    EpisodeBangumiId: number;
    SeasonBangumiId?: number;
    ActualSubjectId?: number;
    Status: 'Mismatch' | 'Unverified';
    Reason: string;
}

interface ScanResult {
    ScannedCount: number;
    TotalItemCount: number;
    CheckedCount: number;
    SkippedCount: number;
    NextStartIndex?: number | null;
    Items: MismatchItem[];
}

export class EpisodeSubjectMismatch extends ToolController {
    private stopped = false;

    mount() {
        this
            .render(`<p>核对普通集实际所属的 Bangumi 条目与 Jellyfin 季度 ID 是否一致，排除特别篇。结果仅供核对，不自动修改元数据。</p>
            <div class="tool-field"><label for="library">媒体库</label><bangumi-select><select id="library"><option value="">全部媒体库</option></select></bangumi-select></div>
            <div class="actions">${action('扫描关系', 'scan')}${action('停止扫描', 'stop', 'secondary')}</div>
            <div id="results"></div>`);
        const stop = this.query<HTMLButtonElement>('#stop');
        stop.hidden = true;
        stop.onclick = () => {
            this.stopped = true;
            stop.disabled = true;
            this.status('正在结束当前批次，已发现的结果会保留。');
        };
        const library = this.query<HTMLSelectElement>('#library');
        library.onchange = () => {
            this.query('#results').replaceChildren();
            this.status('媒体库已改变，请重新扫描。');
        };
        this.query('#scan').onclick = () => this.run(() => this.scan());
        void this.run(async () => {
            const libraries = await this.request<{ Id: string; Name: string }[]>('EpisodeSubjectMismatch/Libraries');
            for (const item of libraries) library.append(new Option(item.Name || '未命名媒体库', item.Id));
            this.status('选择媒体库后开始扫描。分批校验已有单集 ID，离线数据不足时会查询 Bangumi。');
        });
    }

    private async scan() {
        const results = this.query('#results');
        results.replaceChildren();
        const groups = new Map<string, HTMLElement>();
        const libraryId = this.query<HTMLSelectElement>('#library').value;
        const stop = this.query<HTMLButtonElement>('#stop');
        this.stopped = false;
        stop.hidden = false;
        stop.disabled = false;
        let startIndex = 0;
        let scanned = 0;
        let checked = 0;
        let skipped = 0;
        let mismatches = 0;
        let unverified = 0;
        const summary = () =>
            `已校验 ${checked} 项，关系不一致 ${mismatches} 项，未能校验 ${unverified} 项，跳过 ${skipped} 项`;
        try {
            while (true) {
                const query = new URLSearchParams({ startIndex: String(startIndex), limit: '20' });
                if (libraryId) query.set('libraryId', libraryId);
                const page = await this.request<ScanResult>('EpisodeSubjectMismatch/Items?' + query);
                scanned += page.ScannedCount;
                checked += page.CheckedCount;
                skipped += page.SkippedCount;
                for (const item of page.Items) {
                    if (item.Status === 'Mismatch') mismatches++;
                    else unverified++;
                    const key = item.SeasonId || item.Id;
                    let group = groups.get(key);
                    if (!group) {
                        group = document.createElement('section');
                        group.className = 'missing-series';
                        const header = document.createElement('header');
                        header.className = 'result-group-header';
                        const heading = document.createElement('h3');
                        const title = [item.SeriesName, item.SeasonName].filter(Boolean).join(' / ') || '未关联季度';
                        heading.textContent = title;
                        header.append(heading);
                        if (item.SeasonId) header.append(detailsLink(item.SeasonId, title, 'open_in_new'));
                        group.append(header);
                        groups.set(key, group);
                        results.append(group);
                    }
                    const row = document.createElement('div');
                    row.className = 'relation-row';
                    const copy = document.createElement('div');
                    const title = document.createElement('strong');
                    title.textContent = item.Name || '未命名剧集';
                    const description = document.createElement('small');
                    description.textContent = `${item.Status === 'Mismatch' ? '关系不一致' : '未能校验'}：${item.Reason}`;
                    copy.append(title, description);
                    const links = document.createElement('small');
                    links.append(`单集 ${item.EpisodeBangumiId} · 季度条目：`);
                    this.appendSubject(links, item.SeasonBangumiId);
                    links.append(' · 实际所属条目：');
                    this.appendSubject(links, item.ActualSubjectId);
                    const path = document.createElement('small');
                    path.textContent = item.Path || '';
                    copy.append(links, path);
                    row.append(copy, detailsLink(item.Id, item.Name || '剧集'));
                    group.append(row);
                }
                if (this.stopped) {
                    this.status(`扫描已停止（已遍历 ${scanned} / ${page.TotalItemCount} 项）。${summary()}。`);
                    break;
                }
                if (page.NextStartIndex == null) {
                    this.status(
                        `扫描完成。${summary()}。${unverified ? '存在未能校验的项目，请处理后重新扫描。' : ''}`,
                    );
                    break;
                }
                if (page.NextStartIndex <= startIndex) throw new Error('扫描分页未前进，请重新扫描。');
                startIndex = page.NextStartIndex;
                this.status(`正在扫描 ${scanned} / ${page.TotalItemCount} 项。${summary()}。`);
            }
        } catch (error) {
            if (error.name === 'AbortError') throw error;
            throw new Error(`扫描未完成，已发现的结果已保留。${summary()}。${error.message}`);
        } finally {
            stop.hidden = true;
        }
    }

    private appendSubject(container: HTMLElement, id?: number) {
        if (!id) {
            container.append('未知');
            return;
        }
        const link = document.createElement('a');
        link.href = `https://bgm.tv/subject/${id}`;
        link.textContent = String(id);
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        container.append(link);
    }
}

registerTool('bangumi-tool-episode-subject-mismatch', EpisodeSubjectMismatch);
