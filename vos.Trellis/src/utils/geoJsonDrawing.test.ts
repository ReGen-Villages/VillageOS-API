import { describe, it, expect } from 'vitest';
import { GEOJSON_DRAWN_IN } from './geoJsonDrawing';

describe('the widget kinds that draw GeoJSON', () => {
  it('draw it only in a chart\'s own units, so no map draws a density grid\'s outlines', () => {
    const drawing = Object.entries(GEOJSON_DRAWN_IN).filter(([, units]) => units !== null);

    expect(drawing).toEqual([['densityGrid', 'chartUnits']]);
    expect(Object.values(GEOJSON_DRAWN_IN)).not.toContain('map');
  });
});
