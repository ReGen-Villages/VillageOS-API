import { defineConfig } from 'vite'
import { resolve } from 'node:path'

// The report panel on its own, as one module a page with no build step can load: mountFeedback and
// FeedbackRefusedError, with the screenshot library folded in rather than fetched as a second file.
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
