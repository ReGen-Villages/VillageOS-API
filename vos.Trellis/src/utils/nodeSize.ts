// Sigma's `size` attribute is in graph coordinates, not pixels. With tens of
// thousands of nodes packed into a tight disc, large nodes overlap so heavily
// that no internal structure is readable, so the cap stays small.

import { LAYOUT_DEFAULTS } from './guiSettings';

export interface NodeSizeOptions {
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
