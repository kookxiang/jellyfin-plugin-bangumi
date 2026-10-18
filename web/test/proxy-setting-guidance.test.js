import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const settings = await readFile(new URL('../src/settings.html', import.meta.url), 'utf8');
const proxyStart = settings.indexOf('id="ProxyServerUrl"');
const proxyEnd = settings.indexOf('id="BaseServerUrl"', proxyStart);
const proxyField = settings.slice(proxyStart, proxyEnd);

test('proxy setting explains its placeholder and empty-value behavior', () => {
    assert.match(proxyField, /placeholder="请输入代理服务器地址"/);
    assert.match(proxyField, /留空时不启用代理（不使用系统代理或环境变量）/);
    assert.match(proxyField, /示例：http:\/\/127\.0\.0\.1:7890/);
    assert.match(proxyField, /填写并保存后生效/);
});
