import { describe, it, expect } from 'vitest';
import type { BasemapSource } from '../types/basemap';
import { imagerySource, tabSections } from './overviewState';

describe('the tabs of the closing view', () => {
  it('are the sections carrying a tab, in the order the page lists them', () => {
    const spec = {
      sections: [
        { title: 'The land', facts: true, widgets: [] },
        { title: 'Location', tab: 'location', widgets: [] },
        { title: 'Temperature', theme: 'Temperature', widgets: [] },
        { title: 'Water', tab: 'water', widgets: [] },
      ],
    };

    expect(tabSections(spec).map((section) => section.tab)).toEqual(['location', 'water']);
  });
});

describe('the basemap the closing view opens on', () => {
  const streets: BasemapSource = { id: '1', name: 'Streets', attribution: 'a', kind: 'style', styleUrl: 'https://example.test/s.json' };
  const imagery: BasemapSource = { id: '2', name: 'Satellite', attribution: 'b', kind: 'raster', tileUrl: 'https://example.test/{z}/{x}/{y}', maximumZoom: 19 };

  it('is the first source drawing a tile pyramid', () => {
    expect(imagerySource([streets, imagery])).toBe(imagery);
  });

  it('is none where the model declares only styled maps', () => {
    expect(imagerySource([streets])).toBeNull();
  });
});
