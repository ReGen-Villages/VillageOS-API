import { defineConfig } from 'vitest/config';

// Tests that need a running broker, kept out of `npm test` so that runs offline.
// Run with `npm run test:integration`.
export default defineConfig({
  test: {
    include: ['test/integration/**/*.test.ts'],
    environment: 'node',
    // A local broker serves a development certificate; set here alone so no other run accepts one.
    env: { NODE_TLS_REJECT_UNAUTHORIZED: '0' },
  },
});
