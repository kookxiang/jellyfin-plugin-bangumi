import { defineConfig } from 'vite';
import { readFile } from 'node:fs/promises';
import { transform } from 'esbuild';

export default defineConfig({
    plugins: [
        {
            name: 'minify-inline-css',
            apply: 'build',
            enforce: 'pre',
            async load(id) {
                if (!id.endsWith('.css?raw')) return;
                const source = await readFile(id.slice(0, -4), 'utf8');
                const result = await transform(source, {
                    loader: 'css',
                    minify: true,
                    target: 'es2022',
                    legalComments: 'none',
                });
                return { code: `export default ${JSON.stringify(result.code.trim())};`, map: null };
            },
        },
    ],
    server: {
        port: 8765,
        strictPort: true,
        cors: { origin: process.env.BANGUMI_JELLYFIN_ORIGIN || /^https?:\/\/(?:localhost|127\.0\.0\.1)(?::\d+)?$/ },
    },
    build: {
        target: 'es2022',
        minify: 'terser',
        terserOptions: { format: { comments: false } },
        lib: { entry: 'src/main.ts', formats: ['es'], fileName: () => 'bangumi.js' },
        outDir: 'dist',
        emptyOutDir: false,
    },
});
