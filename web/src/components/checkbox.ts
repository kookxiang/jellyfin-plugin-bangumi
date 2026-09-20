import styles from './checkbox.css?raw';

/** Slotted native input and label share their original form and label scope.
 * The component owns presentation; the browser owns state and interaction. */
class BangumiCheckbox extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' }).innerHTML = `<style>${styles}</style>
            <div class="row"><slot name="control"></slot><slot name="label"></slot><slot name="actions"></slot></div>`;
    }
}

if (!customElements.get('bangumi-checkbox')) {
    customElements.define('bangumi-checkbox', BangumiCheckbox);
}
