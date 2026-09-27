import './components/account.ts';
import styles from './user-settings.css?raw';
import type { BangumiOAuthContainer } from './components/account.ts';
import { isOwnPreferencesHash } from './user-settings-route.ts';

const entryId = 'bangumi-user-settings-entry';
const runtimeKey = '__bangumiUserSettingsRuntime';

class BangumiUserSettingsDialog extends HTMLElement {
    #opener: HTMLElement | null = null;
    #onKeyDown = (event: KeyboardEvent) => {
        if (event.key === 'Escape') this.close();
        if (event.key === 'Tab') this.#keepFocusInside(event);
    };

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot!.innerHTML = `<style>${styles}</style>
            <div class="backdrop"></div>
            <section aria-labelledby="bangumi-user-settings-title" aria-modal="true" class="dialog" role="dialog">
                <header class="header">
                    <h2 id="bangumi-user-settings-title">Bangumi 账号</h2>
                    <button aria-label="关闭" class="close" type="button">×</button>
                </header>
                <div class="content"><bangumi-oauth-container></bangumi-oauth-container></div>
            </section>`;
        this.shadowRoot!.querySelector('.backdrop')!.addEventListener('click', () => this.close());
        this.shadowRoot!.querySelector('.close')!.addEventListener('click', () => this.close());
    }

    async open(opener: HTMLElement) {
        this.#opener = opener;
        document.body.append(this);
        const account = this.shadowRoot!.querySelector<BangumiOAuthContainer>('bangumi-oauth-container')!;
        account.configure({ api: window.ApiClient, dashboard: window.Dashboard }, { selfService: true });
        document.addEventListener('keydown', this.#onKeyDown);
        (this.shadowRoot!.querySelector('.close') as HTMLElement).focus();
        await account.show();
    }

    close() {
        document.removeEventListener('keydown', this.#onKeyDown);
        this.shadowRoot?.querySelector<BangumiOAuthContainer>('bangumi-oauth-container')?.hide();
        this.remove();
        this.#opener?.focus();
    }

    #keepFocusInside(event: KeyboardEvent) {
        const focusable: HTMLElement[] = Array.from(
            this.shadowRoot!.querySelectorAll<HTMLElement>('button:not([disabled]), a[href], input:not([disabled])'),
        );
        const account = this.shadowRoot!.querySelector<BangumiOAuthContainer>('bangumi-oauth-container');
        if (account?.shadowRoot) {
            focusable.push(
                ...Array.from(
                    account.shadowRoot.querySelectorAll<HTMLElement>(
                        'button:not([disabled]):not([style*="display: none"]), a[href], input:not([disabled])',
                    ),
                ).filter((element) => element.offsetParent !== null),
            );
        }
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = event.composedPath()[0];
        if (event.shiftKey && active === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && active === last) {
            event.preventDefault();
            first.focus();
        }
    }
}

if (!customElements.get('bangumi-user-settings-dialog')) {
    customElements.define('bangumi-user-settings-dialog', BangumiUserSettingsDialog);
}

function isOwnPreferencesRoute() {
    return isOwnPreferencesHash(
        window.location.hash,
        window.ApiClient?.getCurrentUserId?.(),
        window.location.search,
        window.location.href,
    );
}

function isDisplayed(element: HTMLElement) {
    return element.getClientRects().length > 0;
}

function getPreferencesPage() {
    const pages = Array.from(document.querySelectorAll<HTMLElement>('#myPreferencesMenuPage'));
    return pages.find(isDisplayed) ?? pages.at(-1) ?? null;
}

function getPreferencesList(page: HTMLElement) {
    const sections = Array.from(page.querySelectorAll<HTMLElement>('.verticalSection'));
    return (
        sections.find(
            (section) => !section.classList.contains('adminSection') && !section.classList.contains('userSection'),
        ) ??
        page.querySelector<HTMLElement>('.readOnlyContent') ??
        page
    );
}

function reconcileEntry() {
    const oldEntry = document.getElementById(entryId);
    if (!isOwnPreferencesRoute()) {
        oldEntry?.remove();
        return;
    }
    const page = getPreferencesPage();
    const list = page ? getPreferencesList(page) : null;
    if (!page || !list) {
        oldEntry?.remove();
        return;
    }
    if (oldEntry && list.contains(oldEntry)) return;
    oldEntry?.remove();

    const entry = document.createElement('a');
    entry.id = entryId;
    entry.href = '#/mypreferencesmenu';
    entry.className = 'emby-button listItem-border';
    entry.style.display = 'block';
    entry.style.width = '100%';
    entry.style.margin = '0';
    entry.style.padding = '0';
    entry.innerHTML = `<div class="listItem">
        <span aria-hidden="true" class="material-icons listItemIcon listItemIcon-transparent link"></span>
        <div class="listItemBody">
            <div class="listItemBodyText">Bangumi 账号</div>
            <div class="listItemBodyText secondary">绑定账号并管理播放同步授权</div>
        </div>
    </div>`;
    entry.addEventListener('click', (event) => {
        event.preventDefault();
        const dialog = new BangumiUserSettingsDialog();
        void dialog.open(entry).catch((error) => {
            dialog.close();
            window.Dashboard.alert('无法加载 Bangumi 账号：' + (error?.message || error));
        });
    });
    list.append(entry);
}

function start() {
    const runtime = window as typeof window & { [runtimeKey]?: MutationObserver };
    if (runtime[runtimeKey]) return;
    let queued = false;
    const schedule = () => {
        if (queued) return;
        queued = true;
        requestAnimationFrame(() => {
            queued = false;
            reconcileEntry();
        });
    };
    runtime[runtimeKey] = new MutationObserver(schedule);
    runtime[runtimeKey]!.observe(document.documentElement, { childList: true, subtree: true });
    window.addEventListener('hashchange', schedule);
    window.addEventListener('popstate', schedule);
    document.addEventListener('viewshow', schedule, true);
    document.addEventListener('pageshow', schedule, true);
    const pushState = history.pushState;
    history.pushState = function (data, unused, url) {
        const result = pushState.call(history, data, unused, url);
        schedule();
        return result;
    };
    const replaceState = history.replaceState;
    history.replaceState = function (data, unused, url) {
        const result = replaceState.call(history, data, unused, url);
        schedule();
        return result;
    };
    schedule();
    let retries = 0;
    const retry = window.setInterval(() => {
        schedule();
        retries += 1;
        if (retries >= 20) window.clearInterval(retry);
    }, 300);
}

if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
else start();
