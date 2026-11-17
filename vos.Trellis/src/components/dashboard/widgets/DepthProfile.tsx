import { useState } from 'react';
import type { DepthProfileWidget } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { asRows, nullableNumber, useBindings } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, SeriesTable } from './chartChrome';
import { linearScale, niceTicks } from './chartScale';
import { formatNumber } from './format';

const ROW_HEIGHT = 22;
const ROW_GAP = 6;
const TOP_GUTTER = 4;
const AXIS_BAND = 20;
const LEFT_GUTTER = 84;
const RIGHT_GUTTER = 12;
const FALLBACK_WIDTH = 480;
const TICK_COUNT = 5;
/** A thin ring in the surface colour is the gap between stacked segments. */
const SURFACE_GAP = 1;

type Layer = { label: string; order: number; values: (number | null)[] };

export function DepthProfile({ widget, context }: { widget: DepthProfileWidget; context: ResolveContext }) {
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<string | null>(null);
  const [rows] = useBindings([widget.rows], context);

  const layers: Layer[] = asRows(rows.value)
    .map((row, at) => ({
      label: String(row[widget.labelKey] ?? ''),
      order: widget.orderKey ? (nullableNumber(row[widget.orderKey]) ?? Infinity) : at,
      values: widget.parts.map((part) => nullableNumber(row[part.key])),
    }))
    .filter((layer) => layer.values.some((value) => value !== null))
    .sort((one, other) => one.order - other.order);

  const single = widget.parts.length === 1;
  const totals = layers.map((layer) => layer.values.reduce<number>((sum, value) => sum + (value ?? 0), 0));
  const floor = widget.floor ?? 0;
  const ceiling = widget.ceiling ?? Math.max(floor + 1, ...totals);
  const width = measuredWidth || FALLBACK_WIDTH;
  const x = linearScale([floor, ceiling], [LEFT_GUTTER, width - RIGHT_GUTTER]);
  const plotBottom = TOP_GUTTER + layers.length * (ROW_HEIGHT + ROW_GAP);
  const ticks = niceTicks(floor, ceiling, TICK_COUNT).filter((tick) => tick >= floor && tick <= ceiling);
  const figure = (value: number | null) =>
    value === null ? '—' : `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;
  const readOut = (layer: Layer) =>
    `${layer.label}: ${widget.parts.map((part, at) => `${part.label} ${figure(layer.values[at])}`).join(', ')}`;
  const shownAt = layers.findIndex((layer) => layer.label === active);
  const rowTop = (at: number) => TOP_GUTTER + at * (ROW_HEIGHT + ROW_GAP);

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={plotBottom + AXIS_BAND}
          viewBox={`0 0 ${width} ${plotBottom + AXIS_BAND}`}
          role="img"
          aria-label={widget.title ?? ''}
          className="block max-w-full"
        >
          {ticks.map((tick) => (
            <g key={tick}>
              <line x1={x(tick)} x2={x(tick)} y1={TOP_GUTTER} y2={plotBottom} stroke="var(--chart-grid)" strokeWidth={1} />
              <text x={x(tick)} y={plotBottom + 14} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)" className="tabular-nums">
                {formatNumber(tick, widget.format)}
              </text>
            </g>
          ))}
          {layers.map((layer, at) => {
            const top = rowTop(at);
            let stacked = floor;
            return (
              <g
                key={layer.label}
                data-layer={layer.label}
                role="img"
                tabIndex={0}
                aria-label={readOut(layer)}
                onFocus={() => setActive(layer.label)}
                onBlur={() => setActive(null)}
                onMouseEnter={() => setActive(layer.label)}
                onMouseLeave={() => setActive(null)}
                className="outline-none"
              >
                <rect x={0} y={top} width={width} height={ROW_HEIGHT} fill="transparent" />
                <text x={LEFT_GUTTER - 6} y={top + ROW_HEIGHT / 2 + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)">
                  {layer.label}
                </text>
                {widget.parts.map((part, partAt) => {
                  const value = layer.values[partAt];
                  if (value === null) return null;
                  const from = stacked;
                  const to = single ? value : stacked + value;
                  stacked = to;
                  return (
                    <rect
                      key={part.key}
                      data-part={part.label}
                      x={x(from)}
                      y={top}
                      width={Math.max(0, x(to) - x(from))}
                      height={ROW_HEIGHT}
                      fill={part.colour}
                      stroke="var(--card-bg)"
                      strokeWidth={SURFACE_GAP}
                    />
                  );
                })}
                {active === layer.label && (
                  <rect x={LEFT_GUTTER - 3} y={top - 2} width={width - LEFT_GUTTER - RIGHT_GUTTER + 6} height={ROW_HEIGHT + 4} fill="none" stroke="var(--chart-ink-muted)" strokeWidth={1} rx={3} />
                )}
              </g>
            );
          })}
        </svg>
        {shownAt >= 0 && (
          <ChartTooltip left={LEFT_GUTTER} top={rowTop(shownAt) + ROW_HEIGHT} width={width} title={layers[shownAt].label}>
            {widget.parts.map((part, at) => (
              <div key={part.key} className="flex items-baseline gap-2">
                <PartSwatch colour={part.colour} />
                <span className="font-semibold tabular-nums">{figure(layers[shownAt].values[at])}</span>
                <span className="text-zinc-500 dark:text-zinc-400">{part.label}</span>
              </div>
            ))}
          </ChartTooltip>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
        {widget.parts.map((part) => (
          <li key={part.key} className="inline-flex items-center gap-1.5">
            <PartSwatch colour={part.colour} />
            {part.label}
          </li>
        ))}
      </ul>
      <SeriesTable
        caption={widget.title ?? ''}
        columns={widget.parts.map((part) => part.label)}
        rows={layers.map((layer) => ({ name: layer.label, cells: layer.values.map(figure) }))}
      />
    </WidgetCard>
  );
}

function PartSwatch({ colour }: { colour: string }) {
  return <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: colour }} aria-hidden="true" />;
}
