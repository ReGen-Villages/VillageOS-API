// Sigma's `size` is in graph coordinates, not pixels, and a very large graph is packed into a tight disc,
// so large nodes would hide its structure.

import { LAYOUT_DEFAULTS } from './guiSettings';

interface NodeSizeOptions {
  min: number;
  max: number;
  slope: number;
}

const DEFAULT_OPTIONS: NodeSizeOptions = {
  min: LAYOUT_DEFAULTS.nodeSizeMin,
  max: LAYOUT_DEFAULTS.nodeSizeMax,
  slope: LAYOUT_DEFAULTS.nodeSizeSlope,
};

export function computeNodeSize(degree: number, options: NodeSizeOptions = DEFAULT_OPTIONS): number {
  return Math.max(options.min, Math.min(options.max, options.min + degree * options.slope));
}
