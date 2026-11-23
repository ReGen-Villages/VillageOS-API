import { defineConfig } from 'vite'
import { resolve } from 'node:path'

// One module with the screenshot library inside it, so a page with no build step can load the report panel.
export default defineConfig({
  publicDir: false,
  build: {
    outDir: 'dist-feedback',
    emptyOutDir: true,
    lib: {
      entry: resolve(__dirname, 'src/feedback/feedbackPanel.ts'),
      formats: ['es'],
      fileName: () => 'feedback-widget.js',
    },
    rollupOptions: { output: { inlineDynamicImports: true } },
  },
})
