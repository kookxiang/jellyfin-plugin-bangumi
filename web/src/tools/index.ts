import { pushSectionInUrl, toolFromUrl } from '../navigation-state.ts';
import hostIcons from '../host-icons.css?raw';
import './duplicates.ts';
import './fix-metadata.ts';
import './missing-id.ts';
import './missing-title.ts';
import styles from './tools.css?raw';
import { type ToolElement } from './tool.ts';
import type { Services } from '../types.ts';
const tools = [
    {
        tag: 'bangumi-tool-duplicates',
        title: '重复剧集检测',
        description: '查找同一剧集的多个文件，核对后清理重复内容。',
    },
    { tag: 'bangumi-tool-fix-metadata', title: '修正剧集元数据', description: '移除旧版本产生的无效 Bangumi ID。' },
    {
        tag: 'bangumi-tool-missing-id',
        title: '查找缺失 ID 的视频',
        description: '检查电影和剧集，为所选项目重新获取元数据。',
    },
    {
        tag: 'bangumi-tool-missing-title',
        title: '补全视频标题',
        description: '扫描已有 Bangumi ID 但仍使用文件名的视频，批量刷新新番元数据。',
    },
];
export class BangumiTools extends HTMLElement {
    services: Services;
    syncRoute: () => void = () => {};
    connectedCallback() {
        const root = this.shadowRoot || this.attachShadow({ mode: 'open' });
        root.innerHTML = `<style>${styles}\n${hostIcons}</style><div id="catalogue"><h2 class="catalogue-title">附加工具</h2><div class="cards"></div></div><div id="detail" hidden><header class="tool-header"><bangumi-button variant="quiet" icon><button id="back" type="button" aria-label="返回工具列表" title="返回工具列表"><span class="material-icons" aria-hidden="true">arrow_back</span></button></bangumi-button><h2 id="tool-title"></h2></header><div id="tool"></div></div>`;
        const catalogue = root.querySelector<HTMLElement>('#catalogue')!;
        const cards = root.querySelector<HTMLElement>('.cards')!;
        const detail = root.querySelector<HTMLElement>('#detail')!;
        const mount = root.querySelector<HTMLElement>('#tool')!;
        let current: string | null = null;
        const show = (tag: string | null) => {
            const tool = tools.find((tool) => tool.tag === tag || tool.tag === `bangumi-tool-${tag}`);
            if (current === (tool?.tag || null)) return;
            current = tool?.tag || null;
            if (tool) {
                const component = document.createElement(tool.tag) as ToolElement;
                component.services = this.services;
                root.querySelector('#tool-title')!.textContent = tool.title;
                mount.replaceChildren(component);
            } else mount.replaceChildren();
            catalogue.hidden = !!tool;
            detail.hidden = !tool;
        };
        this.syncRoute = () => {
            if (this.services) show(toolFromUrl(window.location.href));
        };
        root.querySelector<HTMLButtonElement>('#back')!.onclick = () => {
            const previous = window.history.state?.bangumiPreviousUrl;
            if (previous && !toolFromUrl(previous) && new URL(previous).hash.includes('module=tools')) {
                window.history.back();
            } else {
                pushSectionInUrl('tools', window.location, window.history);
                this.syncRoute();
            }
        };
        tools.forEach((tool, index) => {
            const card = document.createElement('button');
            card.type = 'button';
            card.className = 'card';
            card.innerHTML = `<span class="number">0${index + 1}</span><span class="copy"><strong>${tool.title}</strong><small>${tool.description}</small></span><span class="material-icons" aria-hidden="true">chevron_right</span>`;
            card.onclick = () => {
                pushSectionInUrl('tools', window.location, window.history, tool.tag.replace('bangumi-tool-', ''));
                show(tool.tag);
                root.querySelector<HTMLButtonElement>('#back')!.focus();
            };
            cards.append(card);
        });
        const initial = this.getAttribute('initial-tool');
        const index = tools.findIndex((tool) => tool.tag === initial);
        if (index >= 0 && !toolFromUrl(window.location.href)) show(tools[index].tag);
        else this.syncRoute();
    }
}
customElements.define('bangumi-tools', BangumiTools);
