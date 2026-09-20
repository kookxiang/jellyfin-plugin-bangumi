// Selected by DEBUG builds only. Uses the real Jellyfin session and APIs.
const response = await window.ApiClient.fetch({
    type: 'GET',
    url: window.ApiClient.getUrl('Plugins/Bangumi/Web/Development'),
});
if (!response.ok) throw new Error(`Cannot load Bangumi development settings: HTTP ${response.status}`);
const { Url: server } = await response.json();
await import(new URL('@vite/client', server).href);
await import(new URL('src/main.ts', server).href);
