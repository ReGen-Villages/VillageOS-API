import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { Binding, GridMapWidget, GridValue, ResolveContext } from '@tendril-ui/tendril';
import { useBindings } from '@tendril-ui/tendril';
import { readsOnlyThePage } from '../../../utils/geoJsonDrawing';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, SeriesTable } from './chartChrome';
import { rampColour } from './chartRamp';
import { classHolding, drawnClassesOf, type DrawnClass } from './chartClasses';
import { MOST_CELLS_A_MAP_DRAWS, cellBounds, cellsIn, extentOf, gridOf, mercator, outerRingsOf, shareOnScale } from './gridGeometry';
import { formatNumber } from './format';
import { useChartSurface } from './useChartSurface';

const FALLBACK_WIDTH = 480;
const PLOT_HEIGHT = 280;
const LEGEND_STEPS = 24;
const OUTLINE_DASH = '4 3';

type Cell = { row: number; column: number; value: number; x: number; y: number; width: number; height: number };
type Ring = [number, number][];

function layOut(grid: GridValue | null, rings: Ring[], width: number): { cells: Cell[]; outlines: string[] } {
  if (!grid) return { cells: [], outlines: [] };
  const known = grid.values.flatMap((value, at) =>
    value === null ? [] : [{ value, row: Math.floor(at / grid.columns), column: at % grid.columns }]);
  const corners = [
    ...known.flatMap(({ row, column }) => {
      const bounds = cellBounds(grid, row, column);
      return [mercator(bounds.south, bounds.west), mercator(bounds.north, bounds.east)];
    }),
    ...rings.flat().map(([longitude, latitude]) => mercator(latitude, longitude)),
  ];
  const across = extentOf(corners.map((corner) => corner.x));
  const down = extentOf(corners.map((corner) => corner.y));
  if (!across || !down) return { cells: [], outlines: [] };
  const { lowest: left, highest: right } = across;
  const { lowest: top, highest: bottom } = down;
  // One scale for both directions, so a cell keeps the proportions it has on the ground.
  const scale = Math.min(width / (right - left || 1), PLOT_HEIGHT / (bottom - top || 1));
  const offsetX = (width - (right - left) * scale) / 2;
  const offsetY = (PLOT_HEIGHT - (bottom - top) * scale) / 2;
  const place = (latitude: number, longitude: number) => {
    const point = mercator(latitude, longitude);
    return { x: offsetX + (point.x - left) * scale, y: offsetY + (point.y - top) * scale };
  };

  const cells = known.map(({ value, row, column }) => {
    const bounds = cellBounds(grid, row, column);
    const southWest = place(bounds.south, bounds.west);
    const northEast = place(bounds.north, bounds.east);
    return { row, column, value, x: southWest.x, y: northEast.y, width: northEast.x - southWest.x, height: southWest.y - northEast.y };
  });
  const outlines = rings.map((ring) => ring.map(([longitude, latitude], at) => {
    const point = place(latitude, longitude);
    return `${at === 0 ? 'M' : 'L'}${point.x.toFixed(2)},${point.y.toFixed(2)}`;
  }).join(' ') + ' Z');
  return { cells, outlines };
}

export function GridMap({ widget, context }: { widget: GridMapWidget; context: ResolveContext }) {
  const { t } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<Cell | null>(null);
  const surface = useChartSurface();
  const onThePage = (binding: Binding | undefined) => (binding && readsOnlyThePage(binding) ? binding : undefined);
  const [heldGrid, heldOutline, ...classBounds] = useBindings(
    [onThePage(widget.grid), onThePage(widget.outline), ...(widget.classes ?? []).flatMap((entry) => [entry.from, entry.to])],
    context);
  const width = measuredWidth || FALLBACK_WIDTH;
  const scale = widget.scale ?? 'linear';

  // A binding's answer keeps its identity until the next refresh, so these are worked out once per answer.
  const grid = useMemo(() => gridOf(heldGrid.value), [heldGrid.value]);
  const tooManyCells = grid !== null && cellsIn(grid) > MOST_CELLS_A_MAP_DRAWS;
  const drawn = tooManyCells ? null : grid;
  const { cells, outlines } = useMemo(
    () => layOut(drawn, outerRingsOf(heldOutline.value), width), [drawn, heldOutline.value, width]);
  const classKey = JSON.stringify(drawnClassesOf(widget.classes, classBounds));
  const classes = useMemo<DrawnClass[]>(() => JSON.parse(classKey), [classKey]);

  const range = useMemo(() => {
    const known = cells.map((cell) => cell.value);
    return { all: extentOf(known), aboveNought: extentOf(known.filter((value) => value > 0)) };
  }, [cells]);
  const floor = widget.floor ?? (scale === 'logarithmic' ? range.aboveNought?.lowest : range.all?.lowest) ?? 0;
  const ceiling = widget.ceiling ?? range.all?.highest ?? 1;
  const figure = (value: number) => `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;

  // Built apart from the pointer, so moving over a large grid redraws the tooltip and not every cell.
  const drawnCells = useMemo(() => cells.map((cell) => {
    const colour = classes.length > 0
      ? classHolding(classes, cell.value)?.colour
      : rampColour(shareOnScale(cell.value, floor, ceiling, scale), surface);
    if (!colour) return null;
    return (
      <rect
        key={`${cell.row},${cell.column}`}
        data-row={cell.row}
        data-column={cell.column}
        x={cell.x}
        y={cell.y}
        width={cell.width + 0.5}
        height={cell.height + 0.5}
        fill={colour}
        onMouseEnter={() => setActive(cell)}
        onMouseLeave={() => setActive(null)}
      />
    );
  }), [cells, classes, floor, ceiling, scale, surface]);

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      {tooManyCells && grid && (
        <p className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">
          {t('widgets.gridMap.tooManyCells', {
            cells: formatNumber(cellsIn(grid), 'integer'),
            most: formatNumber(MOST_CELLS_A_MAP_DRAWS, 'integer'),
          })}
        </p>
      )}
      <div ref={measure} className="relative mt-2">
        <svg width={width} height={PLOT_HEIGHT} viewBox={`0 0 ${width} ${PLOT_HEIGHT}`} role="img" aria-label={widget.title ?? ''} className="block max-w-full">
          {drawnCells}
          {outlines.map((path, at) => (
            <path
              key={at}
              data-outline
              d={path}
              fill="none"
              stroke="var(--chart-ink)"
              strokeWidth={1.5}
              strokeDasharray={OUTLINE_DASH}
              className="pointer-events-none"
            />
          ))}
        </svg>
        {active && (
          <ChartTooltip
            left={active.x}
            top={active.y}
            width={width}
            title={t('widgets.gridMap.cellAt', { row: active.row + 1, column: active.column + 1 })}
          >
            <span className="tabular-nums">{figure(active.value)}</span>
          </ChartTooltip>
        )}
      </div>
      {classes.length > 0 ? (
        <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
          {classes.map((entry) => (
            <li key={entry.label} className="inline-flex items-center gap-1.5">
              <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: entry.colour }} aria-hidden="true" />
              {entry.label}
            </li>
          ))}
        </ul>
      ) : cells.length > 0 && (
        <div className="mt-2 flex items-center gap-2 text-[11px] text-zinc-500 dark:text-zinc-400 tabular-nums">
          <span>{figure(floor)}</span>
          <span className="flex h-2 flex-1" aria-hidden="true">
            {Array.from({ length: LEGEND_STEPS }, (_, at) => (
              <i key={at} className="h-full flex-1" style={{ background: rampColour(at / (LEGEND_STEPS - 1), surface) }} />
            ))}
          </span>
          <span>{figure(ceiling)}</span>
        </div>
      )}
      {drawn && (
        <SeriesTable
          caption={widget.title ?? ''}
          columns={Array.from({ length: drawn.columns }, (_, column) => String(column + 1))}
          rows={Array.from({ length: drawn.rows }, (_, at) => drawn.rows - 1 - at).map((row) => ({
            name: String(row + 1),
            cells: Array.from({ length: drawn.columns }, (_, column) => {
              const value = drawn.values[row * drawn.columns + column];
              return value === null ? '—' : figure(value);
            }),
          }))}
          hiddenUntilShown
        />
      )}
    </WidgetCard>
  );
}
