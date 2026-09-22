/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * A page that reads the model store has to say what its subscription must cover, or it is shown
 * whatever the page before it asked for — for a page reading across the model, a handful of Things
 * where it expected everything, with nothing on screen to say so.
 *
 * Source text rather than a render: mounting every page would mean standing up each one's whole
 * dependency tree to assert one line. It catches a page that reads the store in its own source,
 * which is how every page here reads it; one reading it only through a component it renders would
 * slip through.
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
