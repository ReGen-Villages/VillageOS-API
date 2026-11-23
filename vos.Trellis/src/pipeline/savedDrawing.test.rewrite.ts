/// <reference types="node" />
import { it } from 'vitest';
import { writeFileSync } from 'node:fs';
import { recordTheSavedDrawing, SAVED_DRAWING_FILE } from './savedDrawing.test.fixture';

// Run by hand with `npm run rewrite:saved-drawing`; the suite's own run never matches this file, so a build cannot rewrite it.
it('rewrites the drawing the engine case runs from what the page saves now', async () => {
  writeFileSync(SAVED_DRAWING_FILE, `${JSON.stringify(await recordTheSavedDrawing(), null, 2)}\n`);
});
