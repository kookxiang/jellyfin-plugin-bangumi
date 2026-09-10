import test from 'node:test';
import assert from 'node:assert/strict';
import { groupMissingTitles } from '../src/tools/missing-title-state.ts';

test('missing titles from different seasons remain in one series group', () => {
    const items = [
        { Id: 'one', SeriesId: 'series', SeriesName: 'Show', Path: '/Show/S1/01.mkv' },
        { Id: 'two', SeriesId: 'series', SeriesName: 'Show', Path: '/Show/S2/01.mkv' },
    ];
    const groups = groupMissingTitles(items);
    assert.equal(groups.length, 1);
    assert.deepEqual(groups[0].items, items);
    assert.equal(groups[0].title, 'Show');
});

test('same named series with different IDs stay separate; videos without a series still appear', () => {
    const groups = groupMissingTitles([
        { Id: 'one', SeriesId: 'first', SeriesName: 'Show' },
        { Id: 'two', SeriesId: 'second', SeriesName: 'Show' },
        { Id: 'movie', SeriesId: '00000000-0000-0000-0000-000000000000' },
    ]);
    assert.equal(groups.length, 3);
    assert.equal(groups[2].items[0].Id, 'movie');
    assert.equal(groups[2].seriesId, undefined);
});
