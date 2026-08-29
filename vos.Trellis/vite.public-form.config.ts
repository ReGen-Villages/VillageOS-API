import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'node:path'

// The public submission form, built on its own. It is served from a public site rather than from the
// broker, so it is a build of its own rather than a route in the application: what a stranger downloads
// is the form and the wizard, and none of the signed-in application. `src/publicForm/noSignedInCode.test.ts`
// is what holds that true as the shared files change.
//
// Addresses are relative so the built directory can be dropped at any path on the site, and the entry is
// emitted as index.html so that path serves it with no rename on the way.
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    {
      name: 'public-form-entry-as-index',
      enforce: 'post',
      generateBundle(_options, bundle) {
        const entry = bundle['public-form.html']
        if (!entry) return
        delete bundle['public-form.html']
        entry.fileName = 'index.html'
        bundle['index.html'] = entry
      },
    },
  ],
  base: './',
  build: {
    outDir: 'dist-public-form',
    emptyOutDir: true,
    // The map library is a chunk of its own and sits above the default warning size. It loads only once
    // a position has been entered, so a person who types their coordinates and stops never downloads it.
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      input: resolve(__dirname, 'public-form.html'),
    },
  },
})
