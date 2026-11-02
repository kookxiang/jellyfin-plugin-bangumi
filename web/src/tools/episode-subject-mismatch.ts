import '../components/select.ts';
import '../components/checkbox.ts';
import '../components/checkbox-group.ts';
import { readEventStream } from './event-stream.ts';
import { detailsLink } from './details-link.ts';
import { ToolController, registerTool, action, checkbox } from './tool.ts';
import styles from './episode-subject-mismatch.css?raw';

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
    Items: MismatchItem[];
}

export class EpisodeSubjectMismatch extends ToolController {
    private scanAbort?: AbortController;

    mount() {
        this.render(`<style>${styles}</style>
            <p class="relation-note">检查剧集与季度的 Bangumi 归属，仅扫描，不修改元数据。</p>
            <div class="relation-toolbar">
                <div class="tool-field"><label for="library">媒体库</label><bangumi-select><select id="library"><option value="">全部媒体库</option></select></bangumi-select></div>
                <div class="relation-toolbar-actions">${action('开始扫描', 'scan')}${action('停止', 'stop', 'secondary')}</div>
            </div>
            <bangumi-checkbox-group class="relation-filters" aria-label="扫描选项">
                ${checkbox('recent-only', '只扫描近一个月内的文件', true)}
                ${checkbox('skip-specials', '跳过特别篇剧集', true)}
            </bangumi-checkbox-group>
            <div id="overview" class="relation-overview" hidden>
                <span class="relation-count-mismatch"><strong id="mismatch-count">0</strong> 归属不符</span>
                <span class="relation-count-unverified"><strong id="unverified-count">0</strong> 未能校验</span>
                <span><strong id="checked-count">0</strong> 已校验</span>
            </div>
            <div id="results" class="relation-results"></div>`);
        const stop = this.query<HTMLButtonElement>('#stop');
        stop.hidden = true;
        stop.onclick = () => {
            this.scanAbort?.abort();
            stop.disabled = true;
        };
        const library = this.query<HTMLSelectElement>('#library');
        const reset = () => {
            this.query('#results').replaceChildren();
            this.query('#overview').hidden = true;
            this.status('筛选条件已改变，请重新扫描。');
        };
        library.onchange = reset;
        this.query<HTMLInputElement>('#recent-only').onchange = reset;
        this.query<HTMLInputElement>('#skip-specials').onchange = reset;
        this.query('#scan').onclick = () => this.run(() => this.scan());
        void this.run(async () => {
            const libraries = await this.request<{ Id: string; Name: string }[]>('EpisodeSubjectMismatch/Libraries');
            for (const item of libraries) library.append(new Option(item.Name || '未命名媒体库', item.Id));
            this.status('选择媒体库和扫描选项后开始扫描。');
        });
    }

    private async scan() {
        const results = this.query('#results');
        results.replaceChildren();
        this.query('#overview').hidden = true;
        const groups = new Map<string, HTMLElement>();
        const libraryId = this.query<HTMLSelectElement>('#library').value;
        const recentOnly = this.query<HTMLInputElement>('#recent-only').checked;
        const skipSpecials = this.query<HTMLInputElement>('#skip-specials').checked;
        const stop = this.query<HTMLButtonElement>('#stop');
        const abort = new AbortController();
        this.scanAbort = abort;
        const signal = this.services.signal ? AbortSignal.any([abort.signal, this.services.signal]) : abort.signal;
        stop.hidden = false;
        stop.disabled = false;
        let total = 0;
        let scanned = 0;
        let checked = 0;
        let skipped = 0;
        let mismatches = 0;
        let unverified = 0;
        const summary = () =>
            `已校验 ${checked} 项，关系不一致 ${mismatches} 项，未能校验 ${unverified} 项，跳过 ${skipped} 项`;
        try {
            const query = new URLSearchParams({ recentOnly: String(recentOnly), skipSpecials: String(skipSpecials) });
            if (libraryId) query.set('libraryId', libraryId);
            // ApiClient.fetch buffers text/* responses and drops AbortSignal in older hosts.
            // Reuse its authentication headers with native fetch to retain streaming and cancellation.
            const headers = { Accept: 'text/event-stream' };
            this.services.api.setRequestHeaders(headers);
            const response = await fetch(
                this.services.api.getUrl('Plugins/Bangumi/Tools/EpisodeSubjectMismatch/Scan?' + query),
                { headers, signal, credentials: 'same-origin' },
            );
            if (!response.ok) throw new Error(await response.text());
            if (!response.headers.get('content-type')?.startsWith('text/event-stream'))
                throw new Error('服务器未返回扫描事件流，请更新插件并刷新页面。');
            await readEventStream(
                response,
                (event, data) => {
                    if (signal.aborted || !this.element.isConnected) return;
                    if (event === 'error') throw new Error((data as { Message: string }).Message);
                    if (event !== 'progress' && event !== 'complete') return;
                    const page = data as ScanResult;
                    scanned = page.ScannedCount;
                    total = page.TotalItemCount;
                    checked = page.CheckedCount;
                    skipped = page.SkippedCount;
                    for (const item of page.Items) {
                        if (item.Status === 'Mismatch') mismatches++;
                        else unverified++;
                        this.appendItem(item, groups, results);
                    }
                    this.query('#overview').hidden = false;
                    this.query('#mismatch-count').textContent = String(mismatches);
                    this.query('#unverified-count').textContent = String(unverified);
                    this.query('#checked-count').textContent = String(checked);
                    this.status(
                        event === 'complete'
                            ? `扫描完成 · 已校验 ${checked} 项，跳过 ${skipped} 项。${!mismatches && !unverified ? '未发现归属问题。' : ''}`
                            : `正在扫描 ${scanned} / ${total} 项。${summary()}。`,
                    );
                },
                signal,
            );
        } catch (error) {
            if (signal.aborted) {
                if (this.element.isConnected)
                    this.status(`扫描已停止（已遍历 ${scanned} / ${total} 项）。${summary()}。`);
                return;
            }
            if (error.name === 'AbortError') throw error;
            throw new Error(`扫描未完成，已发现的结果已保留。${summary()}。${error.message}`);
        } finally {
            abort.abort();
            this.scanAbort = undefined;
            stop.hidden = true;
        }
    }

    override disconnect() {
        this.scanAbort?.abort();
        super.disconnect();
    }

    private appendItem(item: MismatchItem, groups: Map<string, HTMLElement>, results: HTMLElement) {
        const key = item.SeasonId || item.Id;
        let group = groups.get(key);
        if (!group) {
            group = document.createElement('section');
            group.className = 'relation-group';
            const header = document.createElement('header');
            header.className = 'result-group-header';
            const heading = document.createElement('h3');
            heading.textContent = item.SeriesName || '未关联系列';
            const season = document.createElement('span');
            season.className = 'relation-season';
            season.textContent = item.SeasonName || '未关联季度';
            heading.append(season);
            header.append(heading);
            const count = document.createElement('span');
            count.className = 'relation-group-count';
            header.append(count);
            if (item.SeasonId) header.append(detailsLink(item.SeasonId, item.SeasonName || '季度', 'open_in_new'));
            group.append(header);
            groups.set(key, group);
            results.append(group);
        }
        const row = document.createElement('details');
        row.className = 'relation-row';
        row.dataset.status = item.Status;
        const summary = document.createElement('summary');
        const main = document.createElement('span');
        main.className = 'relation-main';
        const name = document.createElement('span');
        name.className = 'relation-name';
        name.textContent = item.Name || '未命名剧集';
        name.title = name.textContent;
        const comparison = document.createElement('span');
        comparison.className = 'relation-comparison';
        for (const [label, id] of [
            ['季度', item.SeasonBangumiId],
            ['单集归属', item.ActualSubjectId],
        ] as const) {
            if (comparison.children.length) {
                const arrow = document.createElement('span');
                arrow.className = 'relation-arrow';
                arrow.textContent = '→';
                arrow.setAttribute('aria-hidden', 'true');
                comparison.append(arrow);
            }
            const value = document.createElement('span');
            value.append(label + ' ');
            const number = document.createElement('b');
            number.textContent = id ? '#' + id : '未知';
            value.append(number);
            comparison.append(value);
        }
        main.append(name, comparison);
        const badge = document.createElement('span');
        badge.className = 'relation-badge';
        badge.textContent = item.Status === 'Mismatch' ? '归属不符' : '未能校验';
        const chevron = document.createElement('span');
        chevron.className = 'relation-chevron';
        chevron.setAttribute('aria-hidden', 'true');
        summary.append(main, badge, chevron);
        const detail = document.createElement('div');
        detail.className = 'relation-detail';
        const metadata = document.createElement('dl');
        for (const [label, value] of [
            ['原因', item.Reason],
            ['文件', item.Path || '无文件路径'],
        ]) {
            const term = document.createElement('dt');
            term.textContent = label;
            const description = document.createElement('dd');
            description.textContent = value;
            metadata.append(term, description);
        }
        const links = document.createElement('div');
        links.className = 'relation-links';
        const episode = document.createElement('a');
        episode.href = `https://bgm.tv/ep/${item.EpisodeBangumiId}`;
        episode.textContent = 'Bangumi 单集 #' + item.EpisodeBangumiId;
        episode.target = '_blank';
        episode.rel = 'noopener noreferrer';
        links.append(episode);
        this.appendSubject(links, item.SeasonBangumiId, '季度条目');
        this.appendSubject(links, item.ActualSubjectId, '单集归属');
        const link = detailsLink(item.Id, item.Name || '剧集');
        link.className = 'relation-item-link';
        link.textContent = '打开剧集详情 ↗';
        links.append(link);
        detail.append(metadata, links);
        row.append(summary, detail);
        group.append(row);
        group.querySelector('.relation-group-count').textContent =
            `${group.querySelectorAll('.relation-row').length} 项`;
    }

    private appendSubject(container: HTMLElement, id: number | undefined, label: string) {
        if (!id) return;
        const link = document.createElement('a');
        link.href = `https://bgm.tv/subject/${id}`;
        link.textContent = `${label} #${id}`;
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        container.append(link);
    }
}

registerTool('bangumi-tool-episode-subject-mismatch', EpisodeSubjectMismatch);
