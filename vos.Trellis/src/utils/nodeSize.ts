// Bug #5361 — node-size formula extracted from graphologyMapper so it can be
// tuned at runtime via GUI_Settings (NodeSizeMin / NodeSizeMax / NodeSizeSlope)
// and unit-tested independently.
//
// Sigma's `size` attribute is in graph coordinates (NOT pixels). At
// cameraRatio = 1 it maps roughly 1:1 to device pixels at the default zoom-out
// level. With ~30k nodes packed into a tight disc (the post-Bug #5358 reality
// for an IFC model), large nodes overlap so heavily that no internal structure
// is readable. Live-browser test against the MV jehrlich seed confirmed that
// dropping max from 15 → 6 reveals the color-bucketed cluster structure.

import { LAYOUT_DEFAULTS } from './guiSettings';

/**
 * Re-exported defaults — back-compat for tests and any caller that doesn't have
 * a LayoutSettings handy. Runtime path uses <c>computeNodeSize(degree, opts)</c>
 * with overrides drawn from LayoutSettings.
 */
export const NODE_SIZE_MIN = LAYOUT_DEFAULTS.nodeSizeMin;
export const NODE_SIZE_MAX = LAYOUT_DEFAULTS.nodeSizeMax;
export const NODE_SIZE_SLOPE = LAYOUT_DEFAULTS.nodeSizeSlope;

export interface NodeSizeOptions {
  min: number;
  max: number;
  slope: number;
}

const DEFAULT_OPTS: NodeSizeOptions = {
  min: NODE_SIZE_MIN,
  max: NODE_SIZE_MAX,
  slope: NODE_SIZE_SLOPE,
};

/**
 * Map an incoming-relationship count (or full degree) to a sigma node size.
 * Pure function — overrides are read from <c>opts</c> when provided, otherwise
 * fall back to the LAYOUT_DEFAULTS-sourced module constants.
 */
export function computeNodeSize(degree: number, opts: NodeSizeOptions = DEFAULT_OPTS): number {
  return Math.max(opts.min, Math.min(opts.max, opts.min + degree * opts.slope));
}
