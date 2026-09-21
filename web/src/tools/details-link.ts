/** Shared, non-mutating detail action for tool result rows and group headings. */
export function detailsLink(id: string, name: string, iconName = 'info_outline'): HTMLAnchorElement {
    const link = document.createElement('a');
    const url = new URL(window.location.href);
    url.hash = '/details?id=' + encodeURIComponent(id);
    link.href = url.href;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    link.className = 'episode-details-link';
    link.title = `在新窗口查看 ${name} 的详情`;
    link.setAttribute('aria-label', link.title);
    const icon = document.createElement('span');
    icon.className = 'material-icons';
    icon.textContent = iconName;
    icon.setAttribute('aria-hidden', 'true');
    link.append(icon);
    return link;
}
