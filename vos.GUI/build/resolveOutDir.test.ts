import { describe, it, expect } from 'vitest';
import { resolveOutDir } from './resolveOutDir';

const GUI = '/repos/VillageOS-API/vos.GUI';
const SIBLING_WWWROOT = '/repos/VillageOS/vos.Broker/wwwroot';

describe('resolveOutDir', () => {
  it('returns the env override when set, ignoring the sibling check', () => {
    expect(
      resolveOutDir({ guiRoot: GUI, envOverride: '/custom/out', pathExists: () => true }),
    ).toBe('/custom/out');
  });

  it('returns the sibling vos.Broker/wwwroot when no env var and the path exists', () => {
    expect(
      resolveOutDir({ guiRoot: GUI, envOverride: undefined, pathExists: (p) => p === SIBLING_WWWROOT }),
    ).toBe(SIBLING_WWWROOT);
  });

  it('falls back to "dist" when no env var and no sibling broker dir exists', () => {
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
