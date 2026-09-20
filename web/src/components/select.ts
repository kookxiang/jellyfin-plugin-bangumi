import styles from './select.css?raw';

/** The native select remains the configuration source; the popup is presentation. */
class BangumiSelect extends HTMLElement {
    #events: AbortController;
    #observer: MutationObserver;
    #select: HTMLSelectElement;
    #button: HTMLButtonElement;
    #list: HTMLDivElement;
    #active = -1;
    #search = '';
    #searchTime = 0;

    constructor() {
        super();
        this.attachShadow({ mode: 'open' }).innerHTML = `<style>${styles}</style><slot></slot>
            <button type="button" role="combobox" aria-haspopup="listbox" aria-expanded="false" aria-controls="options"></button>
            <div id="options" role="listbox" popover="auto"></div>`;
        this.#button = this.shadowRoot.querySelector('button');
        this.#list = this.shadowRoot.querySelector<HTMLDivElement>('[role=listbox]');
        // Declare the invoker so native light-dismiss does not close the popup
        // before a second trigger click can toggle it.
        this.#button.popoverTargetElement = this.#list;
        this.#button.popoverTargetAction = 'toggle';
    }

    connectedCallback() {
        // Wait for children when upgraded during HTML parsing.
        queueMicrotask(() => {
            if (this.isConnected && !this.#events) this.#connect();
        });
    }

    #connect() {
        this.#select = this.querySelector('select');
        if (!this.#select) return;
        this.#events = new AbortController();
        const on = (target, event, callback) =>
            target.addEventListener(event, callback, { signal: this.#events.signal });
        this.#select.tabIndex = -1;
        this.#select.setAttribute('aria-hidden', 'true');
        on(this.#select, 'focus', () => this.#button.focus());
        on(this.#select, 'change', () => this.refresh());
        on(this.#button, 'keydown', (event) => this.#key(event));
        on(this.#list, 'mousedown', (event) => event.preventDefault());
        on(this.#list, 'click', (event) => {
            const option = event.target.closest('[role=option]');
            if (option) this.#choose(Number(option.dataset.index));
        });
        on(this.#list, 'beforetoggle', (event: ToggleEvent) => {
            const opening = event.newState === 'open';
            if (opening) this.#prepareOpen();
            this.#button.setAttribute('aria-expanded', String(opening));
            if (!opening) this.#button.removeAttribute('aria-activedescendant');
        });
        on(this.#list, 'toggle', () => {
            if (this.#list.matches(':popover-open')) this.#highlight(this.#active);
        });
        on(window, 'resize', () => this.#close());
        window.addEventListener(
            'scroll',
            (event) => {
                if (!event.composedPath().includes(this.#list)) this.#close();
            },
            { capture: true, signal: this.#events.signal },
        );
        if (this.#select.form) on(this.#select.form, 'reset', () => queueMicrotask(() => this.refresh()));
        this.#observer = new MutationObserver(() => this.refresh());
        this.#observer.observe(this.#select, { childList: true, subtree: true, attributes: true, characterData: true });
        this.refresh();
    }

    refresh() {
        if (!this.#select) return;
        this.#button.textContent = this.#select.selectedOptions[0]?.textContent || '请选择';
        this.#button.disabled = this.#select.disabled;
        const label = Array.from(this.#select.labels || [])
            .map((label) => label.textContent.trim())
            .join(' ');
        this.#button.setAttribute('aria-label', label || '选择选项');
        this.#list.setAttribute('aria-label', label || '选项');
        const description = this.#select.getAttribute('aria-describedby');
        const text = description
            ?.split(' ')
            .map((id) => (this.#select.getRootNode() as ShadowRoot).getElementById(id)?.textContent.trim())
            .filter(Boolean)
            .join(' ');
        if (text) this.#button.setAttribute('aria-description', text);
        this.#list.replaceChildren(
            ...Array.from(this.#select.options, (option, index) => {
                const item = document.createElement('div');
                item.id = `option-${index}`;
                item.role = 'option';
                item.dataset.index = String(index);
                const title = document.createElement('span');
                title.id = `option-${index}-title`;
                title.textContent = option.textContent;
                item.append(title);
                item.setAttribute('aria-labelledby', title.id);
                if (option.dataset.description) {
                    const description = document.createElement('span');
                    description.className = 'option-description';
                    description.id = `option-${index}-description`;
                    description.textContent = option.dataset.description;
                    item.append(description);
                    item.setAttribute('aria-describedby', description.id);
                }
                item.setAttribute('aria-selected', String(option.selected));
                item.setAttribute('aria-disabled', String(option.disabled));
                return item;
            }),
        );
        if (this.#button.disabled) this.#close();
    }

    #open() {
        if (!this.#button.disabled) this.#list.showPopover();
    }

    #prepareOpen() {
        this.refresh();
        const rect = this.#button.getBoundingClientRect();
        const below = innerHeight - rect.bottom - 12;
        const above = rect.top - 12;
        const upward = below < 220 && above > below;
        Object.assign(this.#list.style, {
            width: `${rect.width}px`,
            left: `${rect.left}px`,
            top: upward ? 'auto' : `${rect.bottom + 6}px`,
            bottom: upward ? `${innerHeight - rect.top + 6}px` : 'auto',
            maxHeight: `${Math.max(44, Math.min(300, upward ? above : below))}px`,
        });
        this.#list.dataset.direction = upward ? 'up' : 'down';
        this.#active = this.#select.selectedIndex;
    }

    #close() {
        if (this.#list.matches(':popover-open')) this.#list.hidePopover();
        this.#button.setAttribute('aria-expanded', 'false');
        this.#button.removeAttribute('aria-activedescendant');
    }

    #highlight(index) {
        this.#active = index;
        for (const item of Array.from(this.#list.children) as HTMLElement[])
            item.classList.toggle('active', Number(item.dataset.index) === index);
        const item = this.#list.children[index] as HTMLElement;
        if (item) {
            this.#button.setAttribute('aria-activedescendant', item.id);
            if (item.offsetTop < this.#list.scrollTop) this.#list.scrollTop = item.offsetTop;
            else if (item.offsetTop + item.offsetHeight > this.#list.scrollTop + this.#list.clientHeight) {
                this.#list.scrollTop = item.offsetTop + item.offsetHeight - this.#list.clientHeight;
            }
        }
    }

    #choose(index) {
        const option = this.#select.options[index];
        if (!option || option.disabled) return;
        const changed = this.#select.selectedIndex !== index;
        this.#select.selectedIndex = index;
        this.refresh();
        this.#close();
        this.#button.focus();
        if (changed) {
            this.#select.dispatchEvent(new Event('input', { bubbles: true }));
            this.#select.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    #key(event) {
        const open = this.#list.matches(':popover-open');
        if (event.key === 'Tab') {
            this.#close();
            return;
        }
        if (event.key === 'Escape') {
            if (open) {
                event.preventDefault();
                this.#close();
            }
            return;
        }
        if (['Enter', ' '].includes(event.key)) {
            event.preventDefault();
            if (open) this.#choose(this.#active);
            else this.#open();
            return;
        }
        const options = Array.from(this.#select.options);
        const enabled = options.map((option, index) => (option.disabled ? -1 : index)).filter((index) => index >= 0);
        if (!enabled.length) return;
        if (['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
            event.preventDefault();
            if (!open) this.#open();
            const position = enabled.indexOf(this.#active);
            const next =
                event.key === 'Home'
                    ? 0
                    : event.key === 'End'
                      ? enabled.length - 1
                      : Math.max(0, Math.min(enabled.length - 1, position + (event.key === 'ArrowDown' ? 1 : -1)));
            this.#highlight(enabled[next]);
        } else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
            event.preventDefault();
            this.#search = Date.now() - this.#searchTime > 700 ? event.key : this.#search + event.key;
            this.#searchTime = Date.now();
            if (!open) this.#open();
            const match = enabled.find((index) =>
                options[index].textContent.trim().toLowerCase().startsWith(this.#search.toLowerCase()),
            );
            if (match !== undefined) this.#highlight(match);
        }
    }

    disconnectedCallback() {
        this.#close();
        this.#events?.abort();
        this.#events = null;
        this.#observer?.disconnect();
    }
}

if (!customElements.get('bangumi-select')) customElements.define('bangumi-select', BangumiSelect);
