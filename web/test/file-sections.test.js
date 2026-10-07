import test from 'node:test';
import assert from 'node:assert/strict';
import { selectSection } from '../src/file-sections.ts';

test('file sections treat brackets literally, support other overrides, and use the first match', () => {
    const sections = [
        { Selector: '[某字幕组][**].mp4', Offset: 26, Skip: true },
        { Selector: '*.mp4', Offset: 0 },
    ];
    assert.deepEqual(selectSection('[某字幕组][01].mp4', sections), sections[0]);
    assert.deepEqual(selectSection('[其他字幕组][01].mp4', sections), sections[1]);
    assert.equal(selectSection('episode-01.mkv', sections), null);
    assert.equal(sections[0].Skip, true);
});
