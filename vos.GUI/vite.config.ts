/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    // Output directory is configurable via VOS_BROKER_WWWROOT env var.
    // Default `dist/` is a local build; set VOS_BROKER_WWWROOT to an absolute
    // path (e.g. /path/to/VillageOS/vos.Broker/wwwroot) to build directly into
    // a broker's wwwroot for local development.
    outDir: process.env.VOS_BROKER_WWWROOT || 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 1000,
    rollupOptions: {
      onwarn(warning, defaultHandler) {
        // @microsoft/signalr ships /*#__PURE__*/ annotations in positions Rollup
        // cannot interpret; the build is unaffected so suppress the noise.
        if (warning.code === 'INVALID_ANNOTATION' && warning.id?.includes('@microsoft/signalr')) return;
        defaultHandler(warning);
      },
      output: {
        manualChunks(id) {
          // three.js + @react-three/* + @thatopen/fragments + three-stdlib
          // are only reachable through React.lazy imports (FragmentsViewer,
          // BuildingDetail3D). Returning undefined opts out of the vendor
          // catch-all so rollup places them in a lazy chunk loaded on demand
          // when the Model or 3D-detail tabs mount.
          //
          // A previous version hoisted these into an eager `vendor-three`
          // chunk, which produced a `vendor-three ↔ vendor` cycle (React
          // came from vendor but vendor-three read React at top level) and
          // broke the production bundle entirely — Bug #5296.
          if (
            id.includes('node_modules/three') ||
            id.includes('node_modules/@react-three') ||
            id.includes('node_modules/@thatopen') ||
            id.includes('node_modules/three-stdlib')
          ) {
            return undefined;
          }
          if (id.includes('node_modules/maplibre-gl') || id.includes('node_modules/@sigma/layer-maplibre')) {
            return 'vendor-map';
          }
          if (id.includes('node_modules/sigma') || id.includes('node_modules/@react-sigma') ||
              id.includes('node_modules/graphology')) {
            return 'vendor-graph';
          }
          if (id.includes('node_modules/')) {
            return 'vendor';
          }
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/setupTests.ts',
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
      '/vosHub': {
        target: 'https://localhost:7243',
        ws: true,
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
