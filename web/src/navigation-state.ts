/** Preserve Jellyfin's hash route and state while recording plugin navigation. */
export function pushSectionInUrl(
    section: string,
    location: Pick<Location, 'href'>,
    history: Pick<History, 'state' | 'pushState'>,
    tool?: string,
) {
    const url = new URL(location.href);
    const hash = url.hash.slice(1);
    const queryIndex = hash.indexOf('?');
    const route = queryIndex < 0 ? hash : hash.slice(0, queryIndex);
    const params = new URLSearchParams(queryIndex < 0 ? '' : hash.slice(queryIndex + 1));
    params.set('module', section);
    if (section === 'tools' && tool) params.set('tool', tool);
    else params.delete('tool');
    url.hash = `${route}?${params}`;
    if (url.href === location.href) return;
    const state = { ...history.state, bangumiPreviousUrl: location.href };
    if (typeof state.idx === 'number') state.idx++;
    history.pushState(state, '', url.href);
}

export function toolFromUrl(href: string): string | null {
    const hash = new URL(href).hash;
    const params = new URLSearchParams(hash.slice(hash.indexOf('?') + 1));
    return params.get('module') === 'tools' ? params.get('tool') : null;
}
