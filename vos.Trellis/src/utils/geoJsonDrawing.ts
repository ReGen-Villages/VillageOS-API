import { SCOPE_REFERENCE, type Binding, type Widget } from '@tendril-ui/tendril';

/** Whether a `value` binding reads the page's own Thing. A related Thing is reached by a relationship or by name,
 *  never as the page itself, so a map holding to this never draws a related Thing's chart-unit shapes on the ground. */
export function readsOnlyThePage(binding: Binding): boolean {
  if (binding.kind !== 'value') return true;
  return (binding.via?.length ?? 0) === 0 && (binding.thing === undefined || binding.thing === SCOPE_REFERENCE);
}

/** Where each widget kind draws a GeoJSON value it binds: in a chart's own units, on a map, or not at all.
 *  A density grid's outlines are GeoJSON in chart units, held on a related Thing, which a map would place on
 *  the ground near nought latitude and longitude, so no kind that draws on a map may read a related Thing.
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
  gridMap: 'map',
  profileLine: null,
  raisedSurface: null,
  lineSeries: null,
  heatmap: null,
  stackedShares: null,
  divergingBar: null,
  smallMultiples: null,
  action: null,
  form: null,
};
