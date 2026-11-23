import { Suspense, lazy, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { RaisedSurfaceWidget, ResolveContext } from '@tendril-ui/tendril';
import { cellsIn, gridOf, useBindings } from '@tendril-ui/tendril';
import { ErrorBoundary } from '../../common/ErrorBoundary';
import { canDrawIn3D } from './canDrawIn3D';
import { WidgetCard } from './WidgetCard';
import { SeriesTable } from './chartChrome';
import { rampColour } from './chartRamp';
import { MOST_CELLS_A_MAP_DRAWS } from './gridGeometry';
import { hasTwoRowsAndColumns, raisedSurfaceOf, type RaisedSurface as Surface } from './surfaceGeometry';
import { formatNumber } from './format';
import { useChartSurface } from './useChartSurface';

const RaisedSurfaceScene = lazy(() => import('./RaisedSurfaceScene'));

const SCENE_HEIGHT = 300;
const LEGEND_STEPS = 24;

function Drawing({ surface, colours, label }: { surface: Surface; colours: string[]; label: string }) {
  const { t } = useTranslation();
  const [drawnIn3D] = useState(canDrawIn3D);
  const notShown = <p className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">{t('widgets.raisedSurface.notShown')}</p>;
  if (!drawnIn3D) return notShown;
  return (
    <ErrorBoundary fallback={notShown}>
      <div role="img" aria-label={label} className="mt-2" style={{ height: SCENE_HEIGHT }}>
        <Suspense fallback={null}>
          <RaisedSurfaceScene surface={surface} colours={colours} />
        </Suspense>
      </div>
    </ErrorBoundary>
  );
}

export function RaisedSurface({ widget, context }: { widget: RaisedSurfaceWidget; context: ResolveContext }) {
  const { t } = useTranslation();
  const theme = useChartSurface();
  const [held] = useBindings([widget.surface], context);
  const grid = useMemo(() => gridOf(held.value), [held.value]);
  const surface = useMemo(() => (grid ? raisedSurfaceOf(grid, widget.exaggeration ?? 1) : null), [grid, widget.exaggeration]);
  const colours = useMemo(() => (surface ? Array.from(surface.shares, (share) => rampColour(share, theme)) : []), [surface, theme]);
  const figure = (value: number) => `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;

  if (!surface || !grid) {
    const whyNot = grid && t(hasTwoRowsAndColumns(grid) ? 'widgets.raisedSurface.noKnownValue' : 'widgets.raisedSurface.notASurface');
    return (
      <WidgetCard title={widget.title} hint={widget.hint}>
        <p className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">{whyNot}</p>
      </WidgetCard>
    );
  }

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      <Drawing
        surface={surface}
        colours={colours}
        label={[widget.title, t('widgets.raisedSurface.range', { lowest: figure(surface.lowest), highest: figure(surface.highest) })].filter(Boolean).join('. ')}
      />
      <div className="mt-2 flex items-center gap-2 text-[11px] text-zinc-500 dark:text-zinc-400 tabular-nums">
        <span>{figure(surface.lowest)}</span>
        <span className="flex h-2 flex-1" aria-hidden="true">
          {Array.from({ length: LEGEND_STEPS }, (_, at) => (
            <i key={at} className="h-full flex-1" style={{ background: rampColour(at / (LEGEND_STEPS - 1), theme) }} />
          ))}
        </span>
        <span>{figure(surface.highest)}</span>
      </div>
      {cellsIn(grid) > MOST_CELLS_A_MAP_DRAWS ? (
        <p className="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
          {t('widgets.raisedSurface.tableTooLarge', {
            cells: formatNumber(cellsIn(grid), 'integer'),
            most: formatNumber(MOST_CELLS_A_MAP_DRAWS, 'integer'),
          })}
        </p>
      ) : (
        <SeriesTable
          caption={widget.title ?? ''}
          columns={Array.from({ length: grid.columns }, (_, column) => String(column + 1))}
          rows={Array.from({ length: grid.rows }, (_, at) => grid.rows - 1 - at).map((row) => ({
            name: String(row + 1),
            cells: Array.from({ length: grid.columns }, (_, column) => {
              const value = grid.values[row * grid.columns + column];
              return value === null ? '—' : figure(value);
            }),
          }))}
          hiddenUntilShown
        />
      )}
    </WidgetCard>
  );
}
