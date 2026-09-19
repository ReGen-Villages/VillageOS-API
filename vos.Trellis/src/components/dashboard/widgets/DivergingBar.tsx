import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { DivergingBarSide, DivergingBarWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { linearScale, niceTicks } from './chartScale';
import { groupsByKey, monthNames, windowOf } from './historySeries';
import { formatNumber } from './format';

const PLOT_HEIGHT = 220;
const TOP_GUTTER = 8;
const AXIS_BAND = 22;
const LEFT_GUTTER = 40;
const RIGHT_GUTTER = 8;
const FALLBACK_WIDTH = 480;
const WIDEST_BAR = 24;
const TICK_COUNT = 3;

export const SIDES = [
  { direction: 'up', fill: 'var(--chart-warm-far)', word: 'above' },
  { direction: 'down', fill: 'var(--chart-cool-far)', word: 'below' },
] as const;
type Direction = (typeof SIDES)[number]['direction'];

export function DivergingBar({ widget, ctx }: { widget: DivergingBarWidget; ctx: ResolveContext }) {
  const { t, i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<number | null>(null);

  const sides: Record<Direction, DivergingBarSide> = { up: widget.up, down: widget.down };
  const resolved = useBindings([widget.up.value, widget.up.threshold, widget.down.value, widget.down.threshold], ctx);
  const groups: Record<Direction, Map<string, number>> = {
    up: groupsByKey(resolved[0]?.value ?? null),
    down: groupsByKey(resolved[2]?.value ?? null),
  };
  const thresholds: Record<Direction, number | null> = {
    up: asNumber(resolved[1]?.value ?? null),
    down: asNumber(resolved[3]?.value ?? null),
  };

  const names = monthNames(i18n.language);
  const months = names
    .map((name, at) => ({ name, at, up: groups.up.get(String(at + 1)), down: groups.down.get(String(at + 1)) }))
    .filter((month) => month.up !== undefined || month.down !== undefined);

  // One scale for both directions, so the taller figure is the taller bar whichever side it is on.
  const reach = Math.max(1, ...months.flatMap((month) => [month.up ?? 0, month.down ?? 0]));
  const width = measuredWidth || FALLBACK_WIDTH;
  const plotTop = TOP_GUTTER;
  const plotBottom = TOP_GUTTER + PLOT_HEIGHT;
  const baseline = (plotTop + plotBottom) / 2;
  const half = PLOT_HEIGHT / 2;
  const heightOf = (figure: number) => (figure / reach) * half;
  const slotWidth = (width - LEFT_GUTTER - RIGHT_GUTTER) / 12;
  const barWidth = Math.min(WIDEST_BAR, slotWidth * 0.6);
  const centreOf = (at: number) => LEFT_GUTTER + slotWidth * (at + 0.5);
  const yUp = linearScale([0, reach], [baseline, plotTop]);
  const yDown = linearScale([0, reach], [baseline, plotBottom]);
  const ticks = niceTicks(0, reach, TICK_COUNT).filter((tick) => tick > 0 && tick <= reach);
  const figure = (value: number | undefined) =>
    value === undefined ? '—' : `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;
  const legendOf = (direction: Direction) => {
    const threshold = thresholds[direction];
    const side = SIDES.find((candidate) => candidate.direction === direction)!;
    return threshold === null
      ? sides[direction].label
      : `${sides[direction].label} · ${t(`widgets.divergingBar.${side.word}`, { threshold: formatNumber(threshold) })}`;
  };
  const readOut = (month: (typeof months)[number]) =>
    `${month.name}: ${sides.up.label} ${figure(month.up)}, ${sides.down.label} ${figure(month.down)}`;
  const shown = active === null ? undefined : months.find((month) => month.at === active);

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.up.value)} />}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={plotBottom + AXIS_BAND}
          viewBox={`0 0 ${width} ${plotBottom + AXIS_BAND}`}
          role="img"
          aria-label={widget.title ?? ''}
          className="block max-w-full"
        >
          {ticks.flatMap((tick) => [
            <g key={`up-${tick}`}>
              <line x1={LEFT_GUTTER} x2={width - RIGHT_GUTTER} y1={yUp(tick)} y2={yUp(tick)} stroke="var(--chart-grid)" strokeWidth={1} />
              <text x={LEFT_GUTTER - 6} y={yUp(tick) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
                {formatNumber(tick, 'integer')}
              </text>
            </g>,
            <g key={`down-${tick}`}>
              <line x1={LEFT_GUTTER} x2={width - RIGHT_GUTTER} y1={yDown(tick)} y2={yDown(tick)} stroke="var(--chart-grid)" strokeWidth={1} />
              <text x={LEFT_GUTTER - 6} y={yDown(tick) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
                {formatNumber(tick, 'integer')}
              </text>
            </g>,
          ])}
          <line data-mark="baseline" x1={LEFT_GUTTER} x2={width - RIGHT_GUTTER} y1={baseline} y2={baseline} stroke="var(--chart-axis)" strokeWidth={1} />
          {months.map((month) => {
            const centre = centreOf(month.at);
            const left = centre - barWidth / 2;
            const up = heightOf(month.up ?? 0);
            const down = heightOf(month.down ?? 0);
            return (
              <g
                key={month.at}
                data-month={String(month.at + 1)}
                role="img"
                tabIndex={0}
                aria-label={readOut(month)}
                onFocus={() => setActive(month.at)}
                onBlur={() => setActive(null)}
                onMouseEnter={() => setActive(month.at)}
                onMouseLeave={() => setActive(null)}
                className="outline-none"
              >
                <rect x={left - 6} y={plotTop} width={barWidth + 12} height={PLOT_HEIGHT} fill="transparent" />
                <rect data-direction="up" x={left} y={baseline - up} width={barWidth} height={up} fill={SIDES[0].fill} />
                <rect data-direction="down" x={left} y={baseline} width={barWidth} height={down} fill={SIDES[1].fill} />
                <text x={centre} y={plotBottom + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                  {month.name}
                </text>
                {active === month.at && (
                  <rect x={left - 3} y={plotTop} width={barWidth + 6} height={PLOT_HEIGHT} fill="none" stroke="var(--chart-ink-muted)" strokeWidth={1} rx={3} />
                )}
              </g>
            );
          })}
        </svg>
        {shown && (
          <ChartTooltip left={centreOf(shown.at)} top={plotTop} width={width} title={shown.name}>
            {SIDES.map((side) => (
              <div key={side.direction} className="flex items-baseline gap-2">
                <Swatch fill={side.fill} />
                <span className="font-semibold tabular-nums">{figure(shown[side.direction])}</span>
                <span className="text-zinc-500 dark:text-zinc-400">{sides[side.direction].label}</span>
              </div>
            ))}
          </ChartTooltip>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
        {SIDES.map((side) => (
          <li key={side.direction} className="inline-flex items-center gap-1.5">
            <Swatch fill={side.fill} />
            {legendOf(side.direction)}
          </li>
        ))}
      </ul>
      <SeriesTable
        caption={widget.title ?? ''}
        columns={[sides.up.label, sides.down.label]}
        rows={months.map((month) => ({ name: month.name, cells: [figure(month.up), figure(month.down)] }))}
      />
    </WidgetCard>
  );
}

function Swatch({ fill }: { fill: string }) {
  return <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: fill }} aria-hidden="true" />;
}
