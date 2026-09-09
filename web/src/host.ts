export function createHost(services: import('./types.ts').Services, root: ShadowRoot) {
    const client = services.api;
    const dashboard = services.dashboard;
    let generation = 0;
    const modules = ['account', 'network', 'metadata', 'episode-parser', 'archive', 'media-library', 'tools'];
    const api = new Proxy(client, {
        get(target, key) {
            if (typeof key === 'symbol') return Reflect.get(target, key);
            if (typeof target[key] !== 'function') return target[key];
            return (...args) => {
                const version = generation;
                const result = target[key](...args);
                if (!result?.then) return result;
                return result.then(
                    (value) => {
                        if (version !== generation) throw new DOMException('页面已关闭', 'AbortError');
                        return value;
                    },
                    (error) => {
                        if (version !== generation) throw new DOMException('页面已关闭', 'AbortError');
                        if (error instanceof Error) throw error;
                        if (error instanceof Response) {
                            throw new Error(`HTTP ${error.status}${error.statusText ? `：${error.statusText}` : ''}`);
                        }
                        throw new Error(typeof error === 'string' ? error : error?.message || '请求失败');
                    },
                );
            };
        },
    });
    return {
        api,
        dashboard,
        modules,
        name: 'Jellyfin',
        supportsField: () => true,
        suspend: () => {
            generation++;
        },
        dialogHelper: {
            createDialog({ id }) {
                const dialog = document.createElement('dialog');
                dialog.id = id;
                dialog.addEventListener('close', () => dialog.remove(), { once: true });
                root.append(dialog);
                return dialog;
            },
            open: (dialog) => dialog.showModal(),
            close: (dialog) => {
                dialog.close();
                dialog.remove();
            },
        },
    };
}
