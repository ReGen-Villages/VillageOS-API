/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { recordTheSavedDrawing, SAVED_DRAWING_FILE } from './savedDrawing.test.fixture';

describe('the drawing the engine case runs', () => {
  it('is what the page saves for it, request for request', async () => {
    const committed = JSON.parse(readFileSync(SAVED_DRAWING_FILE, 'utf8'));

    expect(await recordTheSavedDrawing()).toEqual(committed);
  });
});
