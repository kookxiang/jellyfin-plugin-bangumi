import type { ApiClient } from '../types.ts';
import styles from './episode-preview.css?raw';
import { parseSections, selectSection } from '../file-sections.ts';
interface PreviewResult {
    EpisodeId?: string;
    FileName?: string;
    Parser?: string;
    DetectedIndex?: number;
    Message: string;
}
export class EpisodePreview extends HTMLElement {
    private generation = 0;
    private events: AbortController;
    private sample?: PreviewResult;
    private api: ApiClient;
    private itemId: string;
    private form: HTMLFormElement;
    constructor() {
        super();
        this.attachShadow({
            mode: 'open',
        }).innerHTML = `<style>${styles}</style><header><strong>集数预览</strong><bangumi-button variant="quiet"><button type="button">换一集</button></bangumi-button></header><p id="file"></p><dl><div><dt>识别集数</dt><dd id="detected">—</dd></div><div><dt>Bangumi 集数</dt><dd id="bangumi">—</dd></div><div><dt>Jellyfin 显示</dt><dd id="jellyfin">—</dd></div></dl><p id="status" role="status" aria-live="polite"></p>`;
    }
    configure(api: ApiClient, itemId: string, form: HTMLFormElement) {
        this.events?.abort();
        this.events = new AbortController();
        this.api = api;
        this.itemId = itemId;
        this.form = form;
        this.sample = undefined;
        const update = (event: Event) => {
            if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) this.update();
        };
        form.addEventListener('input', update, { signal: this.events.signal });
        form.addEventListener('change', update, { signal: this.events.signal });
        this.shadowRoot!.querySelector('button')!.onclick = () => {
            void this.load();
        };
        void this.load();
    }
    private field(name: string) {
        return this.form.querySelector<HTMLInputElement>(`#bangumi-media-config-${name}`)!;
    }
    private message(text: string) {
        this.shadowRoot!.querySelector('#status')!.textContent = text;
        for (const id of ['detected', 'bangumi', 'jellyfin'])
            this.shadowRoot!.querySelector(`#${id}`)!.textContent = '—';
    }
    private update() {
        if (!this.sample) return;
        if (!this.field('enabled').checked) {
            this.message('未启用目录配置。');
            return;
        }
        if (!this.field('offset').validity.valid) {
            this.message('请填写整数偏移量。');
            return;
        }
        const detected = this.sample.DetectedIndex;
        if (detected == null) {
            this.message(this.sample.Message);
            return;
        }
        let sections;
        try {
            sections = parseSections(this.field('sections').value);
        } catch (error) {
            this.message(error.message);
            return;
        }
        const selected = selectSection(this.sample.FileName || '', sections);
        if (selected?.Skip ?? this.field('skip').checked) {
            this.message('此文件已设为跳过，不会获取剧集元数据。');
            return;
        }
        const offset = selected?.Offset ?? Number(this.field('offset').value || 0);
        const bangumi = detected - offset;
        const correctIndex = selected?.CorrectIndex ?? this.field('correct-index').checked;
        const jellyfin = correctIndex ? Math.trunc(bangumi) : Math.trunc(bangumi) + offset;
        for (const [id, value] of [
            ['detected', detected],
            ['bangumi', bangumi],
            ['jellyfin', jellyfin],
        ]) {
            this.shadowRoot!.querySelector(`#${id}`)!.textContent = String(value);
        }
        this.shadowRoot!.querySelector('#status')!.textContent =
            bangumi < 0
                ? '偏移后的集数小于 0，请检查偏移量。'
                : `${selected ? `命中 ${selected.Selector}，` : ''}按识别集数计算映射，不验证 Bangumi 中是否存在对应剧集。`;
    }
    private async load() {
        const version = ++this.generation;
        this.sample = undefined;
        this.message('正在识别剧集…');
        this.setAttribute('aria-busy', 'true');
        try {
            const response = await this.api.fetch({
                type: 'GET',
                url: this.api.getUrl(`Plugins/Bangumi/Tools/MediaLibrary/Preview/${this.itemId}`),
            });
            if (!response.ok) throw new Error(await response.text());
            const result: PreviewResult = await response.json();
            if (!this.isConnected || version !== this.generation) return;
            this.sample = result;
            this.shadowRoot!.querySelector('#file')!.textContent = result.FileName
                ? `${result.FileName} · ${result.Parser}`
                : '';
            this.update();
        } catch (error) {
            if (this.isConnected && version === this.generation) this.message(`预览失败：${error.message}`);
        } finally {
            if (version === this.generation) this.removeAttribute('aria-busy');
        }
    }
    disconnectedCallback() {
        this.generation++;
        this.events?.abort();
    }
}
if (!customElements.get('bangumi-episode-preview')) customElements.define('bangumi-episode-preview', EpisodePreview);
