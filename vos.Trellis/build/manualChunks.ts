/**
 * Pure routing function for rollup's `manualChunks` rule. Maps a module id
 * (typically an absolute path inside `node_modules/...`) to a named output
 * chunk, or `undefined` to let rollup decide.
 *
 * Why this is a separate, testable helper:
 * - The chunking rule is load-bearing. Bug #5297 documented how an over-narrow
 *   rule split three.js across chunks and broke `instanceof Camera` identity
 *   for OrbitControls + Fragments raycasting.
 * - Bug #5359 documented the inverse problem: a too-broad catch-all `vendor`
 *   chunk that grew past the 1000 kB warning limit. This helper splits
 *   `react`, `react-dom`, `scheduler` into `vendor-react`, and
 *   `@microsoft/signalr` into `vendor-signalr`, so the residual `vendor`
 *   chunk drops back below the limit and stable libs (react) keep their own
 *   cache key independent of the rest of node_modules.
 *
 * Order matters — the three-ecosystem rule must run first so three-stdlib /
 * three-mesh-bvh don't fall through to `vendor`.
 */
export function pickChunk(id: string): string | undefined {
  // three.js + @react-three/* + @thatopen/* + three-stdlib + three-mesh-bvh
  // share one chunk for `instanceof Camera` identity (Bug #5297).
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

  // Bug #5359 — split react + react-dom + scheduler out of `vendor` so the
  // most stable, largest dep group keeps its own cache key and doesn't get
  // invalidated every time an unrelated node_modules dep changes.
  if (
    /node_modules\/(react|react-dom|scheduler)\//.test(id)
  ) {
    return 'vendor-react';
  }

  // Bug #5359 — @microsoft/signalr is the next-largest single dep in `vendor`
  // and is only needed by the temporal-stream code path. Splitting it lets
  // routes that don't subscribe to SignalR avoid the download cost.
  if (id.includes('node_modules/@microsoft/signalr')) {
    return 'vendor-signalr';
  }

  if (id.includes('node_modules/')) {
    return 'vendor';
  }

  return undefined;
}
