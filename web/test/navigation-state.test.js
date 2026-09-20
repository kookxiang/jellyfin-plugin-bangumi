import test from 'node:test';
import assert from 'node:assert/strict';
import { pushSectionInUrl } from '../src/navigation-state.ts';

test('section changes preserve the host route, parameters and history state', () => {
    const state = { idx: 3, key: 'host-entry' };
    const location = {
        href: 'http://localhost/web/#/configurationpage?name=Plugin.Bangumi.Configuration&module=account&extra=keep',
    };
    const history = {
        state,
        pushState(value, title, href) {
            assert.equal(value.idx, 4);
            assert.equal(value.key, state.key);
            assert.equal(value.bangumiPreviousUrl, location.href);
            location.href = href;
        },
    };
    pushSectionInUrl('archive', location, history);
    const url = new URL(location.href);
    assert.equal(url.hash, '#/configurationpage?name=Plugin.Bangumi.Configuration&module=archive&extra=keep');
    history.pushState = () => assert.fail('unchanged section must not rewrite history');
    pushSectionInUrl('archive', location, history);
});

test('standalone preview retains its query when adding a section', () => {
    const location = { href: 'http://localhost/test/preview.html?production&auth=bound' };
    pushSectionInUrl('metadata', location, {
        state: null,
        pushState(state, title, href) {
            assert.equal(state.bangumiPreviousUrl, location.href);
            assert.equal(href, location.href + '#?module=metadata');
        },
    });
});

test('tool navigation is stored and cleared on section switches', () => {
    const location = {
        href: 'http://localhost/web/#/configurationpage?name=Plugin.Bangumi.Configuration&module=tools',
    };
    const history = {
        state: null,
        pushState(state, title, href) {
            this.state = state;
            location.href = href;
        },
    };
    pushSectionInUrl('tools', location, history, 'missing-id');
    assert.ok(location.href.endsWith('&tool=missing-id'));
    pushSectionInUrl('metadata', location, history);
    assert.ok(!location.href.includes('tool='));
});
