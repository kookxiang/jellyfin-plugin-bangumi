import test from 'node:test';
import assert from 'node:assert/strict';
import { parseOffsetRules, selectOffset } from '../src/offset-rules.ts';

test('file selectors treat brackets literally and use the first match', () => {
    const rules = parseOffsetRules('[某字幕组][**].mp4=26\n*.mp4=0');
    assert.deepEqual(selectOffset('[某字幕组][01].mp4', 3, rules), rules[0]);
    assert.deepEqual(selectOffset('[其他字幕组][01].mp4', 3, rules), rules[1]);
    assert.deepEqual(selectOffset('episode-01.mkv', 3, rules), { Offset: 3, Selector: '' });
});

test('invalid selector lines report their line number', () => {
    assert.throws(() => parseOffsetRules('*.mp4=0\ninvalid'), /第 2 行/);
});
