export function getPreferencesQuery(hash: string, search = '', href = '') {
    const hashQuery = (hash || '').indexOf('?');
    if (hashQuery !== -1) return (hash || '').slice(hashQuery + 1);
    if (search) return search.startsWith('?') ? search.slice(1) : search;
    const hrefQuery = (href || '').indexOf('?');
    if (hrefQuery === -1) return '';
    return href.slice(hrefQuery + 1).split('#', 1)[0];
}

export function isOwnPreferencesHash(hash: string, currentUserId: string, search = '', href = '') {
    if (!currentUserId) return false;
    const route = (hash || '').split('?', 1)[0];
    if (!/^#\/mypreferences(?:menu)?(?:\.html)?\/?$/i.test(route)) return false;
    const target = new URLSearchParams(getPreferencesQuery(hash, search, href)).get('userId');
    if (!target) return true;
    const normalize = (value: string) => value.replace(/-/g, '').toLowerCase();
    return normalize(target) === normalize(currentUserId);
}
