import styles from './segmented-select.css?raw';

/** A single select presented as a native radio group, with one configuration value. */
class BangumiSegmentedSelect extends HTMLElement {
    private select: HTMLSelectElement;
    private observer: MutationObserver;
    private events: AbortController;
    constructor() {
        super();
        this.attachShadow({
            mode: 'open',
        }).innerHTML = `<style>${styles}</style><slot></slot><div role="radiogroup"></div>`;
    }
    connectedCallback() {
        queueMicrotask(() => {
            if (!this.isConnected || this.events) return;
            this.select = this.querySelector('select')!;
            if (!this.select) return;
            this.select.tabIndex = -1;
            this.select.setAttribute('aria-hidden', 'true');
            this.events = new AbortController();
            this.select.addEventListener('change', () => this.refresh(), { signal: this.events.signal });
            this.select.addEventListener(
                'focus',
                () => this.shadowRoot!.querySelector<HTMLInputElement>('input:checked')?.focus(),
                { signal: this.events.signal },
            );
            this.observer = new MutationObserver(() => this.build());
            this.observer.observe(this.select, {
                childList: true,
                subtree: true,
                attributes: true,
                characterData: true,
            });
            this.build();
        });
    }
    private build() {
        const group = this.shadowRoot!.querySelector('[role=radiogroup]')!;
        group.setAttribute(
            'aria-label',
            Array.from(this.select.labels || [])
                .map((label) => label.textContent!.trim())
                .join(' ') || '选择选项',
        );
        group.replaceChildren(
            ...Array.from(this.select.options, (option) => {
                const label = document.createElement('label');
                const radio = document.createElement('input');
                radio.type = 'radio';
                radio.name = 'segment';
                radio.value = option.value;
                const title = document.createElement('span');
                title.textContent = option.textContent;
                label.append(radio, title);
                radio.onchange = () => {
                    if (!radio.checked) return;
                    this.select.value = radio.value;
                    this.refresh();
                    this.select.dispatchEvent(new Event('input', { bubbles: true }));
                    this.select.dispatchEvent(new Event('change', { bubbles: true }));
                };
                return label;
            }),
        );
        this.refresh();
    }
    refresh() {
        if (!this.select) return;
        this.shadowRoot!.querySelectorAll<HTMLInputElement>('input').forEach((radio, index) => {
            radio.checked = this.select.selectedIndex === index;
            radio.disabled = this.select.disabled || this.select.options[index].disabled;
        });
    }
    disconnectedCallback() {
        this.events?.abort();
        this.events = undefined;
        this.observer?.disconnect();
    }
}
if (!customElements.get('bangumi-segmented-select'))
    customElements.define('bangumi-segmented-select', BangumiSegmentedSelect);
