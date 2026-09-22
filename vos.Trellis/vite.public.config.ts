import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'node:path'

// The pages a person with land opens, built on their own: the guided submission form, the findings for
// a submission already made, and the plot-first explore page that runs beside the form so the two
// approaches can be compared. They are served from a public site rather than from the broker, so they
// are a build of their own rather than routes in the application: what a stranger downloads is the
// form, the wizard, the map and the dashboard widgets, and none of the signed-in application.
// `src/publicForm/noSignedInCode.test.ts` is what holds that true as the shared files change.
//
// One build rather than several, so the pages share their chunks — the wizard, the widgets and the
// translations are most of all of them.
//
// Addresses are relative so the built directory can be dropped at any path on the site, and the form is
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
    outDir: 'dist-public',
    emptyOutDir: true,
    // The map library is a chunk of its own and sits above the default warning size. It loads only once
    // a position has been entered, so a person who types their coordinates and stops never downloads it.
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      input: [
        resolve(__dirname, 'public-form.html'),
        resolve(__dirname, 'findings.html'),
        resolve(__dirname, 'explore.html'),
      ],
    },
  },
})
