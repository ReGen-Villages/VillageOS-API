export function pickChunk(id: string): string | undefined {
  // One chunk for every three.js package, so `instanceof Camera` holds between OrbitControls and the Fragments raycaster.
  if (
    /node_modules\/three(-[\w-]+)?\//.test(id) ||
    id.includes('node_modules/@react-three/') ||
    id.includes('node_modules/@thatopen/')
  ) {
    return 'vendor-three';
  }

  if (id.includes('node_modules/maplibre-gl')) {
    return 'vendor-map';
  }

  if (
    id.includes('node_modules/sigma') ||
    id.includes('node_modules/@react-sigma') ||
    id.includes('node_modules/graphology')
  ) {
    return 'vendor-graph';
  }

  // On its own, React stays in the browser's cache when another package changes, and `vendor` stays under the size warning.
  if (
    /node_modules\/(react|react-dom|scheduler)\//.test(id)
  ) {
    return 'vendor-react';
  }

  // A model can name any icon, so the whole set ships, and inside `vendor` it would pass the size warning.
  if (id.includes('node_modules/lucide-react/dist/esm/icons/')) {
    return 'vendor-icons';
  }

  if (id.includes('node_modules/')) {
    return 'vendor';
  }

  return undefined;
}
