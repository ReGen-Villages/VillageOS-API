import { defineConfig, configDefaults } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolveOutDir } from './build/resolveOutDir'
import { pickChunk } from './build/manualChunks'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    // Resolution: VOS_MYCELIUM_WWWROOT > sibling vos.Mycelium/wwwroot > dist/.
    // The sibling auto-detect (Bug #5332) keeps `npm run build` from silently
    // emitting to `dist/` while developers wonder why Mycelium URL still
    // serves a stale bundle.
    outDir: resolveOutDir({ guiRoot: __dirname }),
    emptyOutDir: true,
    // vendor-three is bound by Bug #5297: three + Fragments + three-stdlib
    // + three-mesh-bvh + @react-three/* + @thatopen/* MUST share one chunk
    // for `instanceof Camera` identity. Its minified size sits at ~1.27 MB
    // and cannot be reduced without breaking raycasting. Raise the warning
    // limit to 1500 so vendor-three doesn't trip it; every other chunk is
    // still expected to stay below 1000 kB (Bug #5359 split vendor itself
    // into vendor-react + vendor for that reason).
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      output: {
        // Chunking rule lives in build/manualChunks.ts so it can be unit-
        // tested. See Bug #5297 (three identity) and Bug #5359 (vendor split).
        manualChunks: pickChunk,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/setupTests.ts',
    // test/integration needs a running Mycelium, so it runs from its own config (npm run
    // test:integration) rather than failing every offline run of this one. Added to the defaults
    // rather than replacing them, which would drop the exclusions for dist and the config files.
    exclude: [...configDefaults.exclude, 'test/integration/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.{ts,tsx}', 'src/main.tsx', 'src/setupTests.ts'],
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'https://localhost:7243',
        changeOrigin: true,
        secure: false,
      },
      '/swagger': {
        target: 'https://localhost:7243',
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
