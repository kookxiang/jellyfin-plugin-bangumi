import mediaLibraryStyles from './media-library.css?raw';
import archiveStyles from './archive.css?raw';
import './components/account.ts';
import tabs from './tabs.css?raw';
import './components/episode-preview.ts';
import mediaConfigStyles from './media-config.css?raw';
import './components/segmented-select.ts';
import './components/checkbox-group.ts';
import './tools/index.ts';
import './components/select.ts';
import './components/navigation.ts';
import './components/button.ts';
import './components/checkbox.ts';
import template from './settings.html?raw';
import layout from './layout.css?raw';
import theme from './theme.css?raw';
import icons from './host-icons.css?raw';
import { createController } from './controller.ts';
import { createHost } from './host.ts';

export class JellyfinPluginBangumi extends HTMLElement {
    #controller: ReturnType<typeof createController>;
    #host: ReturnType<typeof createHost>;
    #events: AbortController;
    #mounted = false;
    #saveBarObserver: ResizeObserver;
    // Tests and standalone previews can provide the same host API contract.
    services: import('./types.ts').Services;

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        if (this.#mounted) return;
        this.#mounted = true;
        const services = this.services || { api: window.ApiClient, dashboard: window.Dashboard };
        if (!services.api || !services.dashboard) {
            this.#mounted = false;
            this.shadowRoot.textContent = '无法连接宿主，请重新打开 Bangumi 设置。';
            return;
        }
        this.shadowRoot.innerHTML = `<style>${layout}\n${theme}\n${icons}\n${mediaConfigStyles}${mediaLibraryStyles}\n${tabs}\n${archiveStyles}</style>${template}`;
        this.#host = createHost(services, this.shadowRoot);
        const container = this.shadowRoot.querySelector('#bangumiConfigurationPage');
        // Keep native controls in the same form tree: validation, labels and
        // submission work without a second form-state implementation.
        for (const field of container.querySelectorAll('.inputContainer, .selectContainer, .checkboxContainer')) {
            const control = field.querySelector('input[id], select[id], textarea[id]');
            const description = field.querySelector('.fieldDescription');
            if (control && description && !description.id) {
                description.id = `${control.id}-description`;
                control.setAttribute('aria-describedby', description.id);
            }
        }
        const tools = container.querySelector<import('./tools/index.ts').BangumiTools>('bangumi-tools');
        tools.services = { api: this.#host.api, dashboard: services.dashboard };
        const initialTool = this.getAttribute('tool');
        if (initialTool) {
            container.querySelector('.bangumi-settings-nav-item.active').classList.remove('active');
            container.querySelector('[data-target=tools]').classList.add('active');
            tools.setAttribute('initial-tool', initialTool);
            tools.connectedCallback();
        }
        this.#controller = createController(container, this.#host);
        this.#events = new AbortController();
        const options = { signal: this.#events.signal };
        const content = container.querySelector<HTMLElement>('.bangumi-settings-content');
        const saveBar = container.querySelector<HTMLElement>('.submit-button-container');
        const page = this.closest<HTMLElement>('[data-role="page"], .view') || this;
        const alignSaveBar = () => {
            const pageBounds = page.getBoundingClientRect();
            const left = Math.max(0, pageBounds.left);
            const right = Math.min(document.documentElement.clientWidth, pageBounds.right);
            saveBar.style.setProperty('--save-bar-left', `${left}px`);
            saveBar.style.setProperty('--save-bar-width', `${Math.max(0, right - left)}px`);
        };
        this.#saveBarObserver = new ResizeObserver(alignSaveBar);
        this.#saveBarObserver.observe(content);
        this.#saveBarObserver.observe(this);
        this.#saveBarObserver.observe(page);
        window.addEventListener('resize', alignSaveBar, options);
        window.addEventListener('scroll', alignSaveBar, { ...options, capture: true, passive: true });
        alignSaveBar();
        for (const event of ['viewshow', 'pageshow'])
            page.addEventListener(event, () => this.#controller.show(), options);
        for (const event of ['viewhide', 'pagehide']) page.addEventListener(event, () => this.#hide(), options);
        container.addEventListener(
            'invalid',
            (event) => {
                const panel = (event.target as HTMLElement).closest<HTMLElement>('[data-section]');
                if (panel)
                    container.querySelector<HTMLButtonElement>(`[data-target="${panel.dataset.section}"]`)?.click();
            },
            { ...options, capture: true },
        );
        this.#controller.show();
    }

    #hide() {
        this.#host?.suspend();
        this.#controller?.hide();
    }

    disconnectedCallback() {
        this.#hide();
        this.#events?.abort();
        this.#saveBarObserver?.disconnect();
        this.#controller = null;
        this.#mounted = false;
    }
}

if (!customElements.get('jellyfin-plugin-bangumi')) {
    customElements.define('jellyfin-plugin-bangumi', JellyfinPluginBangumi);
}
