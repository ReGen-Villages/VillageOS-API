import { existsSync } from 'node:fs';
import { resolve } from 'node:path';

/**
 * Pick where the production build emits assets. Resolution order:
 *
 *   1. `VOS_BROKER_WWWROOT` env var — explicit override always wins
 *   2. Smart default: if a sibling `VillageOS/vos.Broker/wwwroot/` directory
 *      exists relative to the GUI repo, use it. This is the dominant local-
 *      dev setup (Bug #5332 — silently emitting to `dist/` was sending
 *      developers down a stale-bundle rabbit hole).
 *   3. Fallback: `dist/` (CI / standalone builds where no sibling broker exists)
 *
 * Pure function — no side effects beyond filesystem stat.
 */
export function resolveOutDir(opts: {
  /** Absolute path of the vos.GUI directory (typically `__dirname` from vite.config.ts). */
  guiRoot: string;
  /** Override for testing. Defaults to process.env.VOS_BROKER_WWWROOT. */
  envOverride?: string;
  /** Override for testing. Defaults to filesystem `existsSync`. */
  pathExists?: (p: string) => boolean;
}): string {
  const envOverride = opts.envOverride ?? process.env.VOS_BROKER_WWWROOT;
  if (envOverride) return envOverride;

  // Sibling layout assumed: <repos>/VillageOS-API/vos.GUI + <repos>/VillageOS/vos.Broker
  const sibling = resolve(opts.guiRoot, '..', '..', 'VillageOS', 'vos.Broker', 'wwwroot');
  const exists = (opts.pathExists ?? existsSync)(sibling);
  if (exists) return sibling;

  return 'dist';
}
