import styles from './tools.css?raw';
import type { Services } from '../types.ts';

export interface ToolElement extends HTMLElement {
    services: Services;
}

export abstract class ToolController {
    protected get services(): Services {
        return this.element.services;
    }
    abstract mount(): void;
    readonly root: ShadowRoot;
    busy = false;
    private generation = 0;
    constructor(protected readonly element: ToolElement) {
        this.root = element.attachShadow({ mode: 'open' });
    }
    render(markup: string) {
        this.root.innerHTML = `<style>${styles}</style>${markup}<p role="status" aria-live="polite"></p>`;
    }
    query<T extends HTMLElement>(selector: string): T {
        return this.root.querySelector<T>(selector)!;
    }
    status(message: string) {
        this.query('[role=status]').textContent = message;
    }
    async request<T>(path: string, data?: Record<string, unknown>): Promise<T> {
        const generation = this.generation;
        const response = await this.services.api.fetch({
            url: this.services.api.getUrl(`Plugins/Bangumi/Tools/${path}`),
            type: data ? 'POST' : 'GET',
            ...(data ? { data } : {}),
        });
        if (!response.ok) throw new Error(await response.text());
        const text = await response.text();
        if (!this.element.isConnected || generation !== this.generation)
            throw new DOMException('工具已关闭', 'AbortError');
        return text ? (JSON.parse(text) as T) : (undefined as T);
    }
    async run(action: () => Promise<void>) {
        if (this.busy) return;
        this.busy = true;
        const controls = Array.from(
            this.root.querySelectorAll<HTMLInputElement | HTMLButtonElement>('button,input,select'),
        );
        const disabled = controls.map((control) => control.disabled);
        controls.forEach((control) => (control.disabled = true));
        this.element.setAttribute('aria-busy', 'true');
        this.status('处理中…');
        try {
            await action();
        } catch (error) {
            if (error.name !== 'AbortError') this.status(`操作失败：${error.message}`);
        } finally {
            this.busy = false;
            this.element.removeAttribute('aria-busy');
            controls.forEach((control, index) => {
                if (control.isConnected) control.disabled = disabled[index];
            });
        }
    }
    confirm(message: string): Promise<boolean> {
        return new Promise((resolve) => this.services.dashboard.confirm(message, '确认操作', resolve));
    }
    disconnect() {
        this.generation++;
    }
}

export function action(label: string, id: string, variant = 'primary'): string {
    return `<bangumi-button variant="${variant}"><button type="button" id="${id}">${label}</button></bangumi-button>`;
}
export function checkbox(id: string, label: string, checked = false): string {
    return `<bangumi-checkbox><input type="checkbox" slot="control" id="${id}" ${checked ? 'checked' : ''}><label slot="label" for="${id}">${label}</label></bangumi-checkbox>`;
}

/** Keep business methods off the custom-element prototype. */
export function registerTool(tag: string, Controller: new (element: ToolElement) => ToolController) {
    if (customElements.get(tag)) return;
    customElements.define(
        tag,
        class extends HTMLElement implements ToolElement {
            services: Services;
            private readonly controller = new Controller(this);
            connectedCallback() {
                this.controller.mount();
            }
            disconnectedCallback() {
                this.controller.disconnect();
            }
        },
    );
}
