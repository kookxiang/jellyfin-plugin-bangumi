import test from 'node:test';
import assert from 'node:assert/strict';
import { getPreferencesQuery, isOwnPreferencesHash } from '../src/user-settings-route.ts';

const current = '01234567-89ab-cdef-0123-456789abcdef';

test('ordinary-user entry is limited to the current user preferences route', () => {
    assert.equal(isOwnPreferencesHash('#/mypreferences', current), true);
    assert.equal(isOwnPreferencesHash('#/mypreferencesmenu', current), true);
    assert.equal(isOwnPreferencesHash('#/mypreferencesmenu.html', current), true);
    assert.equal(isOwnPreferencesHash('#/mypreferences.html?userId=0123456789ABCDEF0123456789ABCDEF', current), true);
    assert.equal(isOwnPreferencesHash('#/mypreferences?userId=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', current), false);
    assert.equal(isOwnPreferencesHash('#/dashboard/users', current), false);
    assert.equal(isOwnPreferencesHash('#/mypreferences', ''), false);
});

test('ordinary-user entry reads userId the same way Jellyfin Web does', () => {
    assert.equal(getPreferencesQuery('#/mypreferencesmenu?userId=abc', '?userId=other'), 'userId=abc');
    assert.equal(getPreferencesQuery('#/mypreferencesmenu', '?userId=abc'), 'userId=abc');
    assert.equal(
        getPreferencesQuery('', '', 'http://localhost/web/index.html?userId=abc#/mypreferencesmenu'),
        'userId=abc',
    );
    assert.equal(
        isOwnPreferencesHash('#/mypreferencesmenu', current, '?userId=0123456789ABCDEF0123456789ABCDEF'),
        true,
    );
    assert.equal(
        isOwnPreferencesHash('#/mypreferencesmenu', current, '?userId=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'),
        false,
    );
});
