import { describe, it, expect } from 'vitest';
import { pickChunk } from './manualChunks';

describe('pickChunk (Bug #5359 — vendor split + Bug #5297 — three identity)', () => {
  describe('vendor-three (Bug #5297 — single-chunk identity)', () => {
    it.each([
      '/repo/node_modules/three/build/three.module.js',
      '/repo/node_modules/three-stdlib/controls/OrbitControls.js',
      '/repo/node_modules/three-mesh-bvh/src/index.js',
      '/repo/node_modules/@react-three/fiber/dist/index.js',
      '/repo/node_modules/@react-three/drei/index.js',
      '/repo/node_modules/@thatopen/fragments/dist/index.js',
    ])('routes %s to vendor-three', (id) => {
      expect(pickChunk(id)).toBe('vendor-three');
    });
  });

  describe('vendor-react (Bug #5359 — stable cache key)', () => {
    it.each([
      '/repo/node_modules/react/index.js',
      '/repo/node_modules/react-dom/client.js',
      '/repo/node_modules/scheduler/cjs/scheduler.production.min.js',
    ])('routes %s to vendor-react', (id) => {
      expect(pickChunk(id)).toBe('vendor-react');
    });
  });

  describe('vendor-graph', () => {
    it.each([
      '/repo/node_modules/sigma/dist/sigma.esm.js',
      '/repo/node_modules/@react-sigma/core/dist/index.js',
      '/repo/node_modules/graphology/dist/graphology.esm.js',
      '/repo/node_modules/graphology-layout-force/worker.js',
    ])('routes %s to vendor-graph', (id) => {
      expect(pickChunk(id)).toBe('vendor-graph');
    });
  });

  describe('vendor-map', () => {
    it.each([
      '/repo/node_modules/maplibre-gl/dist/maplibre-gl.js',
      '/repo/node_modules/maplibre-gl/dist/style-spec/index.js',
    ])('routes %s to vendor-map', (id) => {
      expect(pickChunk(id)).toBe('vendor-map');
    });
  });

  describe('vendor (catch-all)', () => {
    it.each([
      '/repo/node_modules/zod/lib/index.js',
      '/repo/node_modules/zustand/esm/index.js',
      '/repo/node_modules/date-fns/format/index.js',
      '/repo/node_modules/react-router/dist/index.js',
    ])('routes %s to vendor', (id) => {
      expect(pickChunk(id)).toBe('vendor');
    });
  });

  describe('vendor-icons (the set a model names its dashboard icons from)', () => {
    it.each([
      '/repo/node_modules/lucide-react/dist/esm/icons/circle.js',
      '/repo/node_modules/lucide-react/dist/esm/icons/gauge.js',
    ])('routes %s to vendor-icons', (id) => {
      expect(pickChunk(id)).toBe('vendor-icons');
    });

    it('leaves the rest of the icon package in vendor', () => {
      expect(pickChunk('/repo/node_modules/lucide-react/dist/esm/Icon.js')).toBe('vendor');
    });
  });

  describe('app code (no chunk)', () => {
    it.each([
      '/repo/src/main.tsx',
      '/repo/src/components/graph/GraphPage.tsx',
      '/repo/src/utils/fa2Settings.ts',
    ])('returns undefined for %s', (id) => {
      expect(pickChunk(id)).toBeUndefined();
    });
  });

  describe('rule ordering — regression guards', () => {
    it('routes three-stdlib to vendor-three even though it could match the catch-all', () => {
      // Without the leading three(-[\w-]+)? rule, three-stdlib would fall through to
      // `vendor` and split three.js identity across chunks (Bug #5297).
      expect(pickChunk('/x/node_modules/three-stdlib/loaders/GLTFLoader.js')).toBe('vendor-three');
    });

    it('routes react to vendor-react not vendor', () => {
      // Without the explicit react rule, this would hit the node_modules catch-all
      // and rebundle the largest stable dep into the volatile vendor chunk.
      expect(pickChunk('/x/node_modules/react/cjs/react.production.min.js')).toBe('vendor-react');
    });

    it('does NOT route preact, react-router, or react-three to vendor-react', () => {
      // Guards against an over-broad regex like /react/ matching anything with
      // "react" in the path. The rule must match the package boundary.
      expect(pickChunk('/x/node_modules/preact/dist/preact.js')).toBe('vendor');
      expect(pickChunk('/x/node_modules/react-router/dist/index.js')).toBe('vendor');
      // @react-three is already routed to vendor-three by the earlier rule, but
      // verify that staying defensive about ordering doesn't accidentally swap them.
      expect(pickChunk('/x/node_modules/@react-three/fiber/dist/index.js')).toBe('vendor-three');
    });
  });
});
