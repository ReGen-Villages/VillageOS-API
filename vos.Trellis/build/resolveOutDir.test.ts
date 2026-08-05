import { describe, it, expect } from 'vitest';
import { resolve, sep } from 'node:path';
import { resolveOutDir } from './resolveOutDir';

// Resolved rather than written out, so these say which directory is meant instead of how a path is
// spelled: on Windows the same place is drive-qualified and separated by backslashes, and a literal
// POSIX string matched nothing there — the sibling case failed on the agent and nowhere else (#6166).
const GUI = resolve('/repos/VillageOS-API/vos.Trellis');
const SIBLING_WWWROOT = resolve(GUI, '..', '..', 'VillageOS', 'vos.Mycelium', 'wwwroot');

describe('resolveOutDir', () => {
  it('names the sibling wwwroot the build is meant to reach', () => {
    expect(SIBLING_WWWROOT.endsWith(['VillageOS', 'vos.Mycelium', 'wwwroot'].join(sep))).toBe(true);
  });

  it('returns the env override when set, ignoring the sibling check', () => {
    expect(
      resolveOutDir({ guiRoot: GUI, envOverride: '/custom/out', pathExists: () => true }),
    ).toBe('/custom/out');
  });

  it('returns the sibling vos.Mycelium/wwwroot when no env var and the path exists', () => {
    expect(
      resolveOutDir({ guiRoot: GUI, envOverride: undefined, pathExists: (p) => p === SIBLING_WWWROOT }),
    ).toBe(SIBLING_WWWROOT);
  });

  it('falls back to "dist" when no env var and no sibling Mycelium dir exists', () => {
    expect(
      resolveOutDir({ guiRoot: GUI, envOverride: undefined, pathExists: () => false }),
    ).toBe('dist');
  });

  it('treats an empty env var the same as unset', () => {
    expect(
      resolveOutDir({ guiRoot: GUI, envOverride: '', pathExists: () => false }),
    ).toBe('dist');
  });
});
