import { describe, it, expect } from 'vitest';
// Vite `?raw` returns the file's text without evaluating the module, so we can
// assert layout invariants against the JSX source (jsdom has no layout engine
// to detect the pixel overlap this guards against).
import source from './GraphPage.tsx?raw';

/**
 * Bug: on the Graph page the bottom-right filter cluster (PredicateFilterPanel
 * + TypeFilterPanel) grows upward as the predicate list gets long. Its old
 * `max-h-[calc(100vh-1.5rem)]` let it reach `top-3`, right where the top-right
 * control surface lives (the import-fragment action). Sharing `z-10` and coming
 * later in the DOM, the panel painted OVER that control and blocked it.
 *
 * The fix keeps the control surface reachable two independent ways:
 *   1. The control surface stacks above the filter cluster (higher z-index).
 *   2. The filter cluster's max-h reserves a top band so it can't rise into
 *      the control surface region in the first place.
 */

/** Grab the className string of the `absolute` div anchored at the given corner. */
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
    // Old, buggy value only subtracted the 1.5rem of top+bottom gutters, so the
    // panel's top reached top-3 (0.75rem) — flush with the control surface.
    const m = filterCluster.match(/max-h-\[calc\(100vh-([\d.]+)rem\)\]/);
    expect(m, `filter cluster should cap height with calc(100vh-<n>rem): ${filterCluster}`).not.toBeNull();
    const reservedRem = Number(m![1]);
    // The control surface sits at top-3 (0.75rem) and is ~1.75rem tall, so its
    // bottom edge is ~2.5rem from the top. Reserve at least that much, plus the
    // bottom-3 (0.75rem) gutter the panel already sits above.
    expect(reservedRem).toBeGreaterThanOrEqual(2.5);
  });
});
