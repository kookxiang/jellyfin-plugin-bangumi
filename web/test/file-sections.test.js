import test from 'node:test';
import assert from 'node:assert/strict';
import { formatSections, parseSections, selectSection } from '../src/file-sections.ts';

test('file sections treat brackets literally, support other overrides, and use the first match', () => {
    const sections = parseSections(
        '[Section.1]\nSelector=[某字幕组][**].mp4\nOffset=26\nSkip=on\n\n[Section.2]\nSelector=*.mp4\nOffset=0\n',
    );
    assert.deepEqual(selectSection('[某字幕组][01].mp4', sections), sections[0]);
    assert.deepEqual(selectSection('[其他字幕组][01].mp4', sections), sections[1]);
    assert.equal(selectSection('episode-01.mkv', sections), null);
    assert.equal(sections[0].Skip, true);
    assert.deepEqual(parseSections(formatSections(sections)), sections);
});

test('invalid section lines report their line number', () => {
    assert.throws(() => parseSections('[Section.1]\nSelector=*.mp4\ninvalid'), /第 3 行/);
});

test('all per-file switches round trip, including false and zero overrides', () => {
    const sections = parseSections(
        '[Section.2]\nSelector=*.mkv\nID=0\nOffset=0\nReport=off\nSkip=off\nCorrectIndex=on\nType=Special',
    );
    assert.deepEqual(sections, [
        {
            Selector: '*.mkv',
            Id: 0,
            Offset: 0,
            Report: false,
            Skip: false,
            CorrectIndex: true,
            Type: 'Special',
        },
    ]);
    assert.deepEqual(parseSections(formatSections(sections)), sections);
});
