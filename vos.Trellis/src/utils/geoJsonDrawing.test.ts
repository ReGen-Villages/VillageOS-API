import { describe, it, expect } from 'vitest';
import { GEOJSON_DRAWN_IN } from './geoJsonDrawing';

describe('the widget kinds that draw GeoJSON', () => {
  it('are the density grid, in a chart\'s own units, and the grid map, on the ground', () => {
    const drawing = Object.entries(GEOJSON_DRAWN_IN).filter(([, units]) => units !== null);

    expect(drawing).toEqual([['densityGrid', 'chartUnits'], ['gridMap', 'map']]);
  });

  // A kind added here as 'map' needs its own case in the design check's test that a map reads only the page's own Thing.
  it('draw on a map only where the design check holds them to the page\'s own Thing', () => {
    const onAMap = Object.entries(GEOJSON_DRAWN_IN).filter(([, units]) => units === 'map').map(([kind]) => kind);

    expect(onAMap).toEqual(['gridMap']);
  });
});
