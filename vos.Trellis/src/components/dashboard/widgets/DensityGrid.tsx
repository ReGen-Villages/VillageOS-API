import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { Binding, BindingResult, DensityGridAxis, DensityGridWidget } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { asRows, nullableNumber, useBindings } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { FALLBACK_WIDTH } from './chartFrame';
import { rampColour } from './chartRamp';
import { linearScale, niceTicks } from './chartScale';
import { groupsByKey, windowOf } from './historySeries';
import { formatNumber } from './format';
import { useChartSurface } from './useChartSurface';

const PLOT_HEIGHT = 260;
const TOP_GUTTER = 8;
const AXIS_BAND = 34;
const LEFT_GUTTER = 48;
const RIGHT_GUTTER = 8;
const TICKS = 6;
const OUTLINE_FILL_OPACITY = 0.12;

type Cell = { across: number; up: number; count: number; share: number };
type Shape = { label: string; colour: string; closed: boolean; points: [number, number][] };

function cellsOf(groups: Map<string, number>): Cell[] {
  let total = 0;
  for (const count of groups.values()) total += count;
  const cells: Cell[] = [];
  for (const [key, count] of groups) {
    const parts = key.split(',').map(Number);
    if (parts.length !== 2 || parts.some(Number.isNaN)) continue;
    cells.push({ across: parts[0], up: parts[1], count, share: total > 0 ? count / total : 0 });
  }
  return cells;
}

function isPoint(value: unknown): value is [number, number] {
  return Array.isArray(value) && value.length >= 2 && typeof value[0] === 'number' && typeof value[1] === 'number';
}

function shapeOf(value: BindingResult): { closed: boolean; points: [number, number][] } | null {
  let geometry: unknown = value;
  if (typeof value === 'string') {
    try {
      geometry = JSON.parse(value);
    } catch {
      return null;
    }
  }
  if (typeof geometry !== 'object' || geometry === null) return null;
  const { type, coordinates } = geometry as { type?: unknown; coordinates?: unknown };
  if (type === 'Polygon' && Array.isArray(coordinates) && Array.isArray(coordinates[0]) && coordinates[0].every(isPoint))
    return { closed: true, points: coordinates[0] };
  if (type === 'LineString' && Array.isArray(coordinates) && coordinates.every(isPoint))
    return { closed: false, points: coordinates };
  return null;
}

function widthOf(binding: Binding, axis: 'across' | 'up'): number {
  if (binding.kind !== 'history') return 1;
  const width = axis === 'across' ? binding.bins?.width : binding.pairedWith?.bins.width;
  return width && width > 0 ? width : 1;
}

function rangeName(lower: number, width: number, axis: DensityGridAxis): string {
  return `${formatNumber(lower)}–${formatNumber(lower + width)}${axis.unit ? ` ${axis.unit}` : ''}`;
}

function extentOf(values: number[]): [number, number] {
  return values.length === 0 ? [0, 1] : [Math.min(...values), Math.max(...values)];
}

export function DensityGrid({ widget, context }: { widget: DensityGridWidget; context: ResolveContext }) {
  const { t } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<Cell | null>(null);
  const surface = useChartSurface();
  const outlines = widget.outlines ?? [];
  const [counts, coverage, ...outlineValues] = useBindings([widget.cells, widget.coverage, ...outlines.map((outline) => outline.shape)], context);

  const byMonth = widget.cells.kind === 'history' && widget.cells.steps[0]?.fold === 'monthOfYear,cell';
  const cells = byMonth ? [] : cellsOf(groupsByKey(counts.value));
  const acrossWidth = widthOf(widget.cells, 'across');
  const upWidth = widthOf(widget.cells, 'up');
  const shapes: Shape[] = outlines.flatMap((outline, at) => {
    const shape = shapeOf(outlineValues[at]?.value ?? null);
    return shape ? [{ label: outline.label, colour: outline.colour, ...shape }] : [];
  });

  const [acrossFloor, acrossCeiling] = extentOf([
    ...cells.flatMap((cell) => [cell.across, cell.across + acrossWidth]),
    ...shapes.flatMap((shape) => shape.points.map((point) => point[0])),
  ]);
  const [upFloor, upCeiling] = extentOf([
    ...cells.flatMap((cell) => [cell.up, cell.up + upWidth]),
    ...shapes.flatMap((shape) => shape.points.map((point) => point[1])),
  ]);
  const largest = Math.max(0, ...cells.map((cell) => cell.share));

  const width = measuredWidth || FALLBACK_WIDTH;
  const x = linearScale([acrossFloor, acrossCeiling], [LEFT_GUTTER, width - RIGHT_GUTTER]);
  const y = linearScale([upFloor, upCeiling], [TOP_GUTTER + PLOT_HEIGHT, TOP_GUTTER]);
  const height = TOP_GUTTER + PLOT_HEIGHT + AXIS_BAND;
  const figure = (share: number) => formatNumber(share, 'percent');
  const nameOf = (cell: Cell) => `${rangeName(cell.across, acrossWidth, widget.across)}, ${rangeName(cell.up, upWidth, widget.up)}`;
  const path = (shape: Shape) =>
    shape.points.map((point, at) => `${at === 0 ? 'M' : 'L'}${x(point[0]).toFixed(2)},${y(point[1]).toFixed(2)}`).join(' ') + (shape.closed ? ' Z' : '');

  const coverageRow = asRows(coverage.value)[0];
  const unpaired = nullableNumber(coverageRow?.unpairedSamples) ?? 0;
  const pairs = nullableNumber(coverageRow?.samples) ?? 0;
  const mostCommon = cells.reduce<Cell | null>((most, cell) => (most && most.share >= cell.share ? most : cell), null);
  const description = [
    widget.title ?? '',
    mostCommon ? t('widgets.densityGrid.mostCommon', { cell: nameOf(mostCommon), share: figure(mostCommon.share) }) : '',
  ].filter((part) => part !== '').join('. ');

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.cells)} />}>
      {byMonth ? (
        <p className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">{t('widgets.densityGrid.byMonthNotDrawn')}</p>
      ) : (
        <div ref={measure} className="relative mt-2">
          <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={description} className="block max-w-full">
            {niceTicks(upFloor, upCeiling, TICKS).filter((tick) => tick >= upFloor && tick <= upCeiling).map((tick) => (
              <g key={`up-${tick}`}>
                <line x1={LEFT_GUTTER} x2={width - RIGHT_GUTTER} y1={y(tick)} y2={y(tick)} stroke="var(--chart-grid)" strokeWidth={1} />
                <text x={LEFT_GUTTER - 6} y={y(tick) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
                  {formatNumber(tick)}
                </text>
              </g>
            ))}
            {niceTicks(acrossFloor, acrossCeiling, TICKS).filter((tick) => tick >= acrossFloor && tick <= acrossCeiling).map((tick) => (
              <text key={`across-${tick}`} x={x(tick)} y={TOP_GUTTER + PLOT_HEIGHT + 14} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)" className="tabular-nums">
                {formatNumber(tick)}
              </text>
            ))}
            <text x={(LEFT_GUTTER + width - RIGHT_GUTTER) / 2} y={height - 4} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
              {`${widget.across.label}${widget.across.unit ? ` (${widget.across.unit})` : ''}`}
            </text>
            <text x={10} y={TOP_GUTTER + PLOT_HEIGHT / 2} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)" transform={`rotate(-90 10 ${TOP_GUTTER + PLOT_HEIGHT / 2})`}>
              {`${widget.up.label}${widget.up.unit ? ` (${widget.up.unit})` : ''}`}
            </text>
            {cells.map((cell) => (
              <rect
                key={`${cell.across},${cell.up}`}
                data-across={String(cell.across)}
                data-up={String(cell.up)}
                data-share={String(cell.share)}
                x={x(cell.across)}
                y={y(cell.up + upWidth)}
                width={x(cell.across + acrossWidth) - x(cell.across)}
                height={y(cell.up) - y(cell.up + upWidth)}
                fill={rampColour(largest > 0 ? cell.share / largest : 0, surface)}
                onMouseEnter={() => setActive(cell)}
                onMouseLeave={() => setActive(null)}
              />
            ))}
            {shapes.map((shape) => (
              <path
                key={shape.label}
                data-outline={shape.label}
                d={path(shape)}
                fill={shape.closed ? shape.colour : 'none'}
                fillOpacity={shape.closed ? OUTLINE_FILL_OPACITY : undefined}
                stroke={shape.colour}
                strokeWidth={1.5}
                className="pointer-events-none"
              />
            ))}
          </svg>
          {active && (
            <ChartTooltip left={x(active.across)} top={y(active.up + upWidth)} width={width} title={nameOf(active)}>
              <span className="font-semibold tabular-nums">{figure(active.share)}</span>
            </ChartTooltip>
          )}
        </div>
      )}
      {shapes.length > 0 && (
        <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
          {shapes.map((shape) => (
            <li key={shape.label} className="inline-flex items-center gap-1.5">
              <i className="inline-block h-0.5 w-3" style={{ background: shape.colour }} aria-hidden="true" />
              {shape.label}
            </li>
          ))}
        </ul>
      )}
      {unpaired > 0 && (
        <p className="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400">
          {t('widgets.densityGrid.coverage', { pairs, unpaired })}
        </p>
      )}
      <SeriesTable
        caption={widget.title ?? ''}
        columns={[t('widgets.densityGrid.share')]}
        rows={cells.map((cell) => ({ name: nameOf(cell), cells: [figure(cell.share)] }))}
      />
    </WidgetCard>
  );
}
