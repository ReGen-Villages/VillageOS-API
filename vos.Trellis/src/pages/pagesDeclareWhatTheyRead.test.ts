/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * A page reading the model store must say what its subscription covers, or it is shown whatever the page
 * before it asked for, with nothing to say so. Read from source, because mounting every page would stand up
 * each one's dependencies to check one line; a page reading the store only through a component would slip by.
 */

const PAGES = dirname(fileURLToPath(import.meta.url));

/** Reading the store directly, or through the hooks that index it. */
const READS_THE_STORE = /useModelStore|useModelIndex|useDashboards/;

const pages = readdirSync(PAGES).filter((file) => file.endsWith('.tsx') && !file.includes('.test.'));

describe('a page that reads the model store', () => {
  it('was found to scan', () => {
    expect(pages.length, `no pages under ${PAGES}`).toBeGreaterThan(0);
  });

  it('says what its subscription must cover', () => {
    const silent = pages.filter((file) => {
      const source = readFileSync(join(PAGES, file), 'utf8');
      return READS_THE_STORE.test(source) && !source.includes('useSubscription(');
    });

    expect(silent, 'these pages read the model store and declare no subscription').toEqual([]);
  });
});

describe('a page that shows a map', () => {
  it('reaches it through the shared map module, never maplibre-gl itself', () => {
    const importing = pages.filter((file) => readFileSync(join(PAGES, file), 'utf8').includes('maplibre-gl'));

    expect(importing, 'these pages import the map library instead of the shared map module').toEqual([]);
  });
});
