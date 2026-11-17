import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { PolarSharesWidget } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { asRows, nullableNumber, useBindings } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { groupsByKey, monthNames, windowOf } from './historySeries';
import { formatNumber } from './format';

const FALLBACK_WIDTH = 480;
const ONE_ROSE = 260;
const SMALLEST_ROSE = 96;
const ROSE_GAP = 12;
const LABEL_BAND = 16;
/** A thin ring in the surface colour is the gap between stacked wedges. */
const SURFACE_GAP = 1;
const RING_SHARES = [0.25, 0.5, 0.75, 1];
const DEGREES = Math.PI / 180;

type SpeedClass = { order: number; label: string; colour: string };
type Cell = { bearing: number; speed: SpeedClass };
type Rose = { key: string; name: string; total: number; hours: Map<number, Map<number, number>> };

function cellsById(rows: Record<string, unknown>[], widget: PolarSharesWidget): Map<string, Cell> {
  const cells = new Map<string, Cell>();
  for (const row of rows) {
    const bearing = nullableNumber(row[widget.bearingKey]);
    const order = nullableNumber(row[widget.classOrderKey]);
    if (typeof row.id !== 'string' || bearing === null || order === null) continue;
    cells.set(row.id.toLowerCase(), {
      bearing,
      speed: { order, label: String(row[widget.classLabelKey] ?? ''), colour: String(row[widget.classColourKey] ?? '') },
    });
  }
  return cells;
}

/** The hours under each declared cell, a rose for the whole window or one a month, by bearing then class order. */
function rosesOf(counts: Map<string, number>, cells: Map<string, Cell>, byMonth: boolean, months: string[], whole: string): Rose[] {
  const roses = new Map<string, Rose>();
  for (const [key, hours] of counts) {
    const [month, cellId] = byMonth ? [key.slice(0, key.indexOf(',')), key.slice(key.indexOf(',') + 1)] : ['all', key];
    const cell = cells.get(cellId.toLowerCase());
    if (!cell) continue;
    const rose = roses.get(month) ?? { key: month, name: byMonth ? months[Number(month) - 1] : whole, total: 0, hours: new Map() };
    roses.set(month, rose);
    const sector = rose.hours.get(cell.bearing) ?? new Map<number, number>();
    rose.hours.set(cell.bearing, sector);
    sector.set(cell.speed.order, (sector.get(cell.speed.order) ?? 0) + hours);
    rose.total += hours;
  }
  return [...roses.values()].sort((one, other) => Number(one.key) - Number(other.key));
}

function wedge(centre: number, bearing: number, halfWidth: number, inner: number, outer: number): string {
  const point = (angle: number, radius: number) =>
    `${(centre + radius * Math.sin(angle * DEGREES)).toFixed(2)},${(centre - radius * Math.cos(angle * DEGREES)).toFixed(2)}`;
  const from = bearing - halfWidth;
  const to = bearing + halfWidth;
  return `M${point(from, inner)} L${point(from, outer)} A${outer},${outer} 0 0 1 ${point(to, outer)} L${point(to, inner)} A${inner},${inner} 0 0 0 ${point(from, inner)} Z`;
}

export function PolarShares({ widget, context }: { widget: PolarSharesWidget; context: ResolveContext }) {
  const { t, i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<{ rose: string; bearing: number } | null>(null);
  const [counts, cellRows] = useBindings([widget.counts, widget.cells], context);

  const cells = cellsById(asRows(cellRows.value), widget);
  const byMonth = widget.counts.kind === 'history' && widget.counts.steps[0]?.fold === 'monthOfYear,value';
  const months = monthNames(i18n.language);
  const roses = rosesOf(groupsByKey(counts.value), cells, byMonth, months, t('widgets.history.annual'));
  const classes = [...new Map([...cells.values()].map((cell) => [cell.speed.order, cell.speed])).values()]
    .sort((one, other) => one.order - other.order);
  const bearings = [...new Set([...cells.values()].map((cell) => cell.bearing))].sort((one, other) => one - other);
  const halfWidth = bearings.length > 0 ? 180 / bearings.length : 0;
  const shareOf = (rose: Rose, bearing: number, order: number) => (rose.hours.get(bearing)?.get(order) ?? 0) / (rose.total || 1);
  const reachOf = (rose: Rose, bearing: number) => classes.reduce((sum, speed) => sum + shareOf(rose, bearing, speed.order), 0);
  const largest = Math.max(0, ...roses.flatMap((rose) => bearings.map((bearing) => reachOf(rose, bearing))));

  const width = measuredWidth || FALLBACK_WIDTH;
  const size = roses.length > 1 ? Math.max(SMALLEST_ROSE, Math.floor((width - ROSE_GAP * 3) / 4)) : Math.min(width, ONE_ROSE);
  const radius = size / 2 - 4;
  const centre = size / 2;
  const scale = (share: number) => (largest > 0 ? (share / largest) * radius : 0);
  const figure = (share: number) => formatNumber(share, 'percent');
  const readOut = (rose: Rose, bearing: number) =>
    `${bearing}°: ${classes.map((speed) => `${speed.label} ${figure(shareOf(rose, bearing, speed.order))}`).join(', ')}`;
  const shown = active && roses.find((rose) => rose.key === active.rose);

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.counts)} />}>
      <div ref={measure} className="relative mt-2 flex flex-wrap gap-3">
        {roses.map((rose) => (
          <figure key={rose.key} data-month={byMonth ? rose.key : undefined} className="m-0">
            <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label={`${widget.title ?? ''} ${rose.name}`} className="block">
              {RING_SHARES.map((ring) => (
                <circle key={ring} cx={centre} cy={centre} r={ring * radius} fill="none" stroke="var(--chart-grid)" strokeWidth={1} />
              ))}
              {bearings.map((bearing) => {
                let reached = 0;
                return (
                  <g
                    key={bearing}
                    data-sector={String(bearing)}
                    role="img"
                    tabIndex={0}
                    aria-label={readOut(rose, bearing)}
                    onFocus={() => setActive({ rose: rose.key, bearing })}
                    onBlur={() => setActive(null)}
                    onMouseEnter={() => setActive({ rose: rose.key, bearing })}
                    onMouseLeave={() => setActive(null)}
                    className="outline-none"
                  >
                    {classes.map((speed) => {
                      const share = shareOf(rose, bearing, speed.order);
                      if (share === 0) return null;
                      const inner = scale(reached);
                      reached += share;
                      const outer = scale(reached);
                      return (
                        <path
                          key={speed.order}
                          data-class={speed.label}
                          data-outer={outer}
                          d={wedge(centre, bearing, halfWidth, inner, outer)}
                          fill={speed.colour}
                          stroke="var(--card-bg)"
                          strokeWidth={SURFACE_GAP}
                        />
                      );
                    })}
                  </g>
                );
              })}
            </svg>
            {byMonth && (
              <figcaption className="text-center text-[11px] text-zinc-500 dark:text-zinc-400" style={{ height: LABEL_BAND }}>
                {rose.name}
              </figcaption>
            )}
          </figure>
        ))}
        {shown && active && (
          <ChartTooltip left={0} top={0} width={width} title={`${shown.name}, ${active.bearing}°`}>
            {[...classes].reverse().map((speed) => (
              <div key={speed.order} className="flex items-baseline gap-2">
                <ClassSwatch colour={speed.colour} />
                <span className="font-semibold tabular-nums">{figure(shareOf(shown, active.bearing, speed.order))}</span>
                <span className="text-zinc-500 dark:text-zinc-400">{speed.label}</span>
              </div>
            ))}
          </ChartTooltip>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
        {classes.map((speed) => (
          <li key={speed.order} className="inline-flex items-center gap-1.5">
            <ClassSwatch colour={speed.colour} />
            {speed.label}
          </li>
        ))}
      </ul>
      <SeriesTable
        caption={widget.title ?? ''}
        columns={classes.map((speed) => speed.label)}
        rows={roses.flatMap((rose) => bearings.map((bearing) => ({
          name: byMonth ? `${rose.name} ${bearing}°` : `${bearing}°`,
          cells: classes.map((speed) => figure(shareOf(rose, bearing, speed.order))),
        })))}
      />
    </WidgetCard>
  );
}

function ClassSwatch({ colour }: { colour: string }) {
  return <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: colour }} aria-hidden="true" />;
}
