import { defineConfig, configDefaults } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolveOutDir } from './build/resolveOutDir'
import { pickChunk } from './build/manualChunks'
import { TENDRIL_SHARES } from './build/tendrilShares'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { dedupe: TENDRIL_SHARES },
  build: {
    outDir: resolveOutDir({ guiRoot: __dirname }),
    emptyOutDir: true,
    // vendor-three has to stay one chunk and is above the default warning size; every other chunk stays below it.
    chunkSizeWarningLimit: 1500,
    rollupOptions: {
      output: {
        manualChunks: pickChunk,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/setupTests.ts',
    // These need a running broker, so they have their own configuration; the defaults stay, so dist stays excluded.
    exclude: [...configDefaults.exclude, 'test/integration/**'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura'],
      reportsDirectory: './coverage',
      // Tendril's sources sit outside this folder, and this suite's tests run its binding reader.
      allowExternal: true,
      include: ['src/**/*.{ts,tsx}', '../vos.Tendril/src/**/*.{ts,tsx}'],
      exclude: [
        'src/**/*.test.{ts,tsx}',
        '../vos.Tendril/src/**/*.test.{ts,tsx}',
        '../vos.Tendril/src/setupTests.ts',
        'src/main.tsx',
        'src/setupTests.ts',
      ],
    },
  },
  server: {
    port: 5173,
    fs: { allow: ['.', '../vos.Tendril'] },
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
      '/basemaps': {
        target: 'https://localhost:7243',
        changeOrigin: true,
        secure: false,
      },
      // The feedback relay on its default port, with the prefix taken off as the deployment's proxy does.
      '/feedback': {
        target: 'http://localhost:7310',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/feedback/, ''),
      },
    },
  },
})
