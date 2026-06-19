import { existsSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * Pick where the production build emits assets. Resolution order:
 *   1. `VOS_MYCELIUM_WWWROOT` env override
 *   2. Sibling `VillageOS/vos.Mycelium/wwwroot/` if it exists (dominant local-dev
 *      setup; emitting to `dist/` instead caused stale-bundle confusion, Bug #5332)
 *   3. `dist/` (CI / standalone builds)
 */
export function resolveOutDir(opts: {
  guiRoot: string;
  envOverride?: string;
  pathExists?: (p: string) => boolean;
}): string {
  const envOverride = opts.envOverride ?? process.env.VOS_MYCELIUM_WWWROOT;
  if (envOverride) return envOverride;

  const sibling = resolve(opts.guiRoot, '..', '..', 'VillageOS', 'vos.Mycelium', 'wwwroot');
  const exists = (opts.pathExists ?? existsSync)(sibling);
  if (exists) return sibling;

  return 'dist';
}
