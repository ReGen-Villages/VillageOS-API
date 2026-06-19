/**
 * Maps a module id to a named rollup output chunk, or `undefined` to defer.
 * Order matters: the three-ecosystem rule must run first so three-stdlib /
 * three-mesh-bvh don't fall through to `vendor`.
 */
export function pickChunk(id: string): string | undefined {
  // Three.js ecosystem shares one chunk for `instanceof Camera` identity
  // across OrbitControls + Fragments raycasting (Bug #5297).
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

  if (
    id.includes('node_modules/sigma') ||
    id.includes('node_modules/@react-sigma') ||
    id.includes('node_modules/graphology')
  ) {
    return 'vendor-graph';
  }

  // React kept on its own cache key so unrelated node_modules churn doesn't
  // invalidate it, and to keep `vendor` under the size warning limit (Bug #5359).
  if (
    /node_modules\/(react|react-dom|scheduler)\//.test(id)
  ) {
    return 'vendor-react';
  }

  if (id.includes('node_modules/')) {
    return 'vendor';
  }

  return undefined;
}
