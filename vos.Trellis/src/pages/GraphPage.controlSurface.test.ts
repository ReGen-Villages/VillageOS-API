import { describe, it, expect } from 'vitest';
// Read as text, because jsdom has no layout to show the overlap this guards against.
import source from './GraphPage.tsx?raw';

/** The filter cluster grows upward with a long predicate list and must not cover the control surface at top
 *  right, so the control surface stacks above it and the cluster's height leaves a band at the top. */

function classNameForAnchor(anchor: string): string {
  const re = new RegExp(`className="(absolute ${anchor}[^"]*)"`);
  const m = source.match(re);
  if (!m) throw new Error(`No absolute container anchored at "${anchor}" found`);
  return m[1];
}

function zIndex(className: string): number {
  const m = className.match(/z-(\d+)/);
  if (!m) throw new Error(`No z-index in "${className}"`);
  return Number(m[1]);
}

describe('GraphPage control surface vs. filter cluster', () => {
  const controlSurface = classNameForAnchor('top-3 right-3');
  const filterCluster = classNameForAnchor('bottom-3 right-3');

  it('stacks the top-right control surface above the filter cluster', () => {
    expect(zIndex(controlSurface)).toBeGreaterThan(zIndex(filterCluster));
  });

  it('reserves a top band so the filter cluster cannot rise into the controls', () => {
    // Taking off only the gutters would let the panel's top reach the control surface.
    const m = filterCluster.match(/max-h-\[calc\(100vh-([\d.]+)rem\)\]/);
    expect(m, `filter cluster should cap height with calc(100vh-<n>rem): ${filterCluster}`).not.toBeNull();
    const reservedRem = Number(m![1]);
    // The control surface's bottom edge is about 2.5rem from the top.
    expect(reservedRem).toBeGreaterThanOrEqual(2.5);
  });
});
