import { existsSync } from 'node:fs';
import { resolve } from 'node:path';

// The broker serves the page from its sibling wwwroot, so a local build that wrote only to `dist/` left the broker serving an old one.
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
