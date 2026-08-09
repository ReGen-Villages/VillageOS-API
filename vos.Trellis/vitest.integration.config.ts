import { defineConfig } from 'vitest/config';

// Tests that need a running Mycelium, kept out of `npm test` so the offline suite stays offline.
// Run with `npm run test:integration`.
export default defineConfig({
  test: {
    include: ['test/integration/**/*.test.ts'],
    environment: 'node',
    // A local Mycelium serves a development certificate, which is why the dev server's proxy sets
    // `secure: false` for the same host. Scoped to this config so no other run relaxes it.
    env: { NODE_TLS_REJECT_UNAUTHORIZED: '0' },
  },
});
