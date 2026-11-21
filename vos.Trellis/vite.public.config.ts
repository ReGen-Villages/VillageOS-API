import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'node:path'
import { TENDRIL_SHARES } from './build/tendrilShares'

// The public pages are a build of their own, so a stranger downloads none of the signed-in application;
// `src/publicPages/noSignedInCode.test.ts` checks that. One build for all of them, so they share chunks.
// Relative addresses and an index.html let the built folder serve from any path on the site.
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    {
      name: 'public-empty-entry-as-index',
      enforce: 'post',
      generateBundle(_options, bundle) {
        const entry = bundle['public-empty.html']
        if (!entry) return
        delete bundle['public-empty.html']
        entry.fileName = 'index.html'
        bundle['index.html'] = entry
      },
    },
  ],
  resolve: { dedupe: TENDRIL_SHARES },
  base: './',
  build: {
    outDir: 'dist-public',
    emptyOutDir: true,
    rollupOptions: {
      input: [resolve(__dirname, 'public-empty.html')],
    },
  },
})
