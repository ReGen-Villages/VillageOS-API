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
          // go into a single `vendor-three` chunk. Without an explicit rule,
          // rollup duplicated three.js across FragmentsViewer, OrbitControls,
          // and vendor chunks — each carrying its own `class Camera`
          // definition, so `instanceof Camera` checks across chunks failed.
          // That broke OrbitControls zoom events and Fragments raycast
          // picking — Bug #5297.
          //
          // The earlier Bug #5296 variant split the same group into an
          // eager chunk that clashed with React (cycle vendor ↔ vendor-three
          // → React is undefined). Because nothing in this app statically
          // imports three (every entry point is React.lazy), the named
          // chunk remains in the lazy graph and the cycle doesn't recur.
          // Match the whole three.js ecosystem: three itself, three-stdlib,
          // three-mesh-bvh (Fragments uses it internally for raycasting),
          // @react-three/*, @thatopen/* — all must share a single chunk or
          // `instanceof Camera` checks across three.js module instances
          // break raycasting + OrbitControls. Any `three-*` prefix qualifies.
          if (
            /node_modules\/three(-[\w-]+)?\//.test(id) ||
            id.includes('node_modules/@react-three/') ||
            id.includes('node_modules/@thatopen/')
          ) {
            return 'vendor-three';
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
