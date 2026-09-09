import './checkbox.ts';
import styles from './checkbox-group.css?raw';

/** Groups presentation only; each checkbox keeps its native input and label. */
class BangumiCheckboxGroup extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' }).innerHTML = `<style>${styles}</style><slot></slot>`;
    }
    connectedCallback() {
        if (!this.hasAttribute('role')) this.setAttribute('role', 'group');
    }
}
if (!customElements.get('bangumi-checkbox-group')) {
    customElements.define('bangumi-checkbox-group', BangumiCheckboxGroup);
}
