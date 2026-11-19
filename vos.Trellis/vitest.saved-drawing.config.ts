import { defineConfig, mergeConfig } from 'vitest/config';
import viteConfig from './vite.config';

export default mergeConfig(viteConfig, defineConfig({
  test: { include: ['src/pipeline/savedDrawing.test.rewrite.ts'] },
}));
