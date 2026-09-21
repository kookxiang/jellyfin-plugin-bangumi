import test from 'node:test';
import assert from 'node:assert/strict';
import { createHost } from '../src/host.ts';
import { collectConfiguration } from '../src/configuration.ts';

test('configuration preserves unknown fields and native types, ignoring tool fields', () => {
    const field = (id, value, type = 'text', ignore = false) => ({
        id,
        value,
        type,
        checked: value,
        hasAttribute: () => ignore,
    });
    assert.deepEqual(
        collectConfiguration({ RequestTimeout: 5000, Enabled: true, SecretSetting: { a: 1 }, Pattern: 'old' }, [
            field('RequestTimeout', '10000'),
            field('Enabled', false, 'checkbox'),
            field('Pattern', '^new$'),
            field('Search', 'test'),
            field('SecretSetting', 'overwrite', 'text', true),
        ]),
        { RequestTimeout: 10000, Enabled: false, SecretSetting: { a: 1 }, Pattern: '^new$' },
    );
});

test('host binds API methods to client and invalidates late reads when page hides', async () => {
    let resolve;
    const client = {
        name: 'server',
        getUrl() {
            return this.name;
        },
        getJSON() {
            return new Promise((r) => {
                resolve = r;
            });
        },
    };
    const host = createHost({ api: client, dashboard: {} }, null);
    assert.equal(host.api.getUrl(), 'server');
    const request = host.api.getJSON();
    host.suspend();
    resolve({ stale: true });
    await assert.rejects(request, { name: 'AbortError' });
    const next = host.api.getJSON();
    resolve({ fresh: true });
    assert.deepEqual(await next, { fresh: true });
});

test('host turns rejected HTTP responses into useful errors and ignores stale failures', async () => {
    const response = new Response(null, { status: 500, statusText: 'Internal Server Error' });
    const host = createHost({ api: { fetch: () => Promise.reject(response) }, dashboard: {} }, null);
    await assert.rejects(host.api.fetch(), { message: 'HTTP 500：Internal Server Error' });
    const stale = host.api.fetch();
    host.suspend();
    await assert.rejects(stale, { name: 'AbortError' });
});

test('library opt-in keeps a string array and supports deselecting every library', () => {
    const controls = ['library-1', 'library-2', 'library-3'].map((value, index) => ({
        id: 'library-' + index,
        name: 'EnabledMissingEpisodeLibraries',
        type: 'checkbox',
        value,
        checked: index < 2,
        hasAttribute: () => false,
    }));
    const config = { EnabledMissingEpisodeLibraries: ['old-library'] };
    assert.deepEqual(collectConfiguration(config, controls).EnabledMissingEpisodeLibraries, ['library-1', 'library-2']);
    for (const control of controls) control.checked = false;
    assert.deepEqual(collectConfiguration(config, controls).EnabledMissingEpisodeLibraries, []);
});
