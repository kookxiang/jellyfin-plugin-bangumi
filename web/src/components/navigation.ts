import styles from './navigation.css?raw';

// Native buttons stay in the settings tree so routing and validation share
// the same section-switching path. The component owns only presentation.
class BangumiNavigation extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' }).innerHTML = `<style>${styles}</style><slot></slot>`;
    }
}

if (!customElements.get('bangumi-navigation')) {
    customElements.define('bangumi-navigation', BangumiNavigation);
}
