import styles from './button.css?raw';

/** Keep the native button in its form; variants only own presentation. */
class BangumiButton extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' }).innerHTML = `<style>${styles}</style><slot></slot>`;
    }
}

if (!customElements.get('bangumi-button')) {
    customElements.define('bangumi-button', BangumiButton);
}
