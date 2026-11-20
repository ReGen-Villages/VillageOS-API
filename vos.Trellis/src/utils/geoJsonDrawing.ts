import type { Widget } from '@tendril-ui/tendril';

/** Where each widget kind draws a GeoJSON value it binds: in a chart's own units, on a map, or not at all.
 *  A study keeps its density-grid outlines as GeoJSON in chart units, which a map would place on the
 *  ground near nought latitude and longitude, so no kind that draws on a map may bind a study property.
 *  Every kind is listed, so a new kind does not build until it says which it is. */
export const GEOJSON_DRAWN_IN: Record<Widget['type'], 'chartUnits' | 'map' | null> = {
  kpi: null,
  funnel: null,
  bullet: null,
  table: null,
  gantt: null,
  leaderboard: null,
  verdict: null,
  working: null,
  exceptionBar: null,
  rangeBar: null,
  boxPlot: null,
  depthProfile: null,
  polarShares: null,
  densityGrid: 'chartUnits',
  lineSeries: null,
  heatmap: null,
  stackedShares: null,
  divergingBar: null,
  smallMultiples: null,
  action: null,
  form: null,
};
