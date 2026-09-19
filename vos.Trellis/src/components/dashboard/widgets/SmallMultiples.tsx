import { useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { SmallMultiplesWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { linearScale, paddedDomain } from './chartScale';
import { groupsByKey, monthNames, windowOf } from './historySeries';
import { formatNumber } from './format';

const HOURS = 24;
const PANEL_HEIGHT = 96;
const PANEL_GAP = 12;
const LABEL_BAND = 14;
const FALLBACK_WIDTH = 480;
const BAR_FILL = 'var(--chart-cool-near)';
const LINE_STROKE = 'var(--chart-warm-far)';

/** How many panels sit in a row at a width: six across a wide card, four on a tablet, three on a phone. */
function columnsFor(width: number): number {
  return width >= 720 ? 6 : width >= 480 ? 4 : 3;
}

type Panel = { name: string; month: number; bars: (number | undefined)[]; line: (number | undefined)[] };

/** The figures of one series by month and hour, off groups keyed "month,hour" as the platform keys a
 *  composite fold. */
function byMonth(groups: Map<string, number>): Map<number, (number | undefined)[]> {
  const months = new Map<number, (number | undefined)[]>();
  for (const [key, value] of groups) {
    const [month, hour] = key.split(',').map(Number);
    if (!Number.isInteger(month) || !Number.isInteger(hour) || hour < 0 || hour >= HOURS) continue;
    const hours = months.get(month) ?? Array.from({ length: HOURS }, () => undefined);
    hours[hour] = value;
    months.set(month, hours);
  }
  return months;
}

export function SmallMultiples({ widget, ctx }: { widget: SmallMultiplesWidget; ctx: ResolveContext }) {
  const { i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<{ month: number; hour: number } | null>(null);

  const resolved = useBindings([widget.bars.value, widget.line.value, widget.band?.from, widget.band?.to], ctx);
  const bars = byMonth(groupsByKey(resolved[0]?.value ?? null));
  const line = byMonth(groupsByKey(resolved[1]?.value ?? null));
  const band = widget.band
    ? { label: widget.band.label, colour: widget.band.colour, from: asNumber(resolved[2]?.value ?? null), to: asNumber(resolved[3]?.value ?? null) }
    : null;

  const names = monthNames(i18n.language);
  const panels: Panel[] = names
    .map((name, at) => ({ name, month: at + 1, bars: bars.get(at + 1) ?? [], line: line.get(at + 1) ?? [] }))
    .filter((panel) => panel.bars.length > 0 || panel.line.length > 0);

  // Two scales: the bars fill the panel from nought to their highest figure, the line spans its own
  // figures and the band's bounds, so a humidity and a temperature each read against an axis of their own.
  const barFigures = panels.flatMap((panel) => panel.bars).filter((figure): figure is number => figure !== undefined);
  const lineFigures = [
    ...panels.flatMap((panel) => panel.line).filter((figure): figure is number => figure !== undefined),
    ...(band ? [band.from, band.to].filter((bound): bound is number => bound !== null) : []),
  ];
  const barDomain: [number, number] = [0, barFigures.length ? Math.max(...barFigures) : 1];
  const lineDomain = paddedDomain(lineFigures.length ? [Math.min(...lineFigures), Math.max(...lineFigures)] : [0, 1]);

  const width = measuredWidth || FALLBACK_WIDTH;
  const columns = columnsFor(width);
  const rows = Math.ceil(panels.length / columns);
  const panelWidth = (width - PANEL_GAP * (columns - 1)) / columns;
  const barWidth = panelWidth / HOURS;
  const height = rows * (PANEL_HEIGHT + LABEL_BAND) + Math.max(0, rows - 1) * PANEL_GAP;
  const yBars = linearScale(barDomain, [PANEL_HEIGHT, 0]);
  const yLine = linearScale(lineDomain, [PANEL_HEIGHT, 0]);
  const xOf = (hour: number) => hour * barWidth;

  const figure = (side: 'bars' | 'line', value: number | undefined) =>
    value === undefined ? '—' : `${formatNumber(value, widget[side].format)}${widget[side].unit ? ` ${widget[side].unit}` : ''}`;
  const readOut = (panel: Panel) => {
    const range = (series: (number | undefined)[], side: 'bars' | 'line') => {
      const known = series.filter((figure): figure is number => figure !== undefined);
      return known.length ? `${figure(side, Math.min(...known))} – ${figure(side, Math.max(...known))}` : '—';
    };
    return `${panel.name}: ${widget.bars.label} ${range(panel.bars, 'bars')}, ${widget.line.label} ${range(panel.line, 'line')}`;
  };
  const shown = active === null ? undefined : panels.find((panel) => panel.month === active.month);

  const panelOrigin = (index: number) => ({
    left: (index % columns) * (panelWidth + PANEL_GAP),
    top: Math.floor(index / columns) * (PANEL_HEIGHT + LABEL_BAND + PANEL_GAP),
  });

  const walk = (panel: Panel) => (event: KeyboardEvent<SVGGElement>) => {
    const hour = active?.month === panel.month ? active.hour : 0;
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
    if (step === 0 && event.key !== 'Home' && event.key !== 'End') return;
    event.preventDefault();
    const next = event.key === 'Home' ? 0 : event.key === 'End' ? HOURS - 1 : (hour + step + HOURS) % HOURS;
    setActive({ month: panel.month, hour: next });
  };

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.bars.value)} />}>
      <div ref={measure} className="relative mt-2">
        <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={widget.title ?? ''} className="block max-w-full">
          {panels.map((panel, index) => {
            const { left, top } = panelOrigin(index);
            const points = panel.line
              .map((value, hour) => (value === undefined ? null : `${xOf(hour) + barWidth / 2},${yLine(value)}`))
              .filter((point): point is string => point !== null);
            return (
              <g
                key={panel.month}
                transform={`translate(${left},${top})`}
                data-month={String(panel.month)}
                data-plot-height={PANEL_HEIGHT}
                role="img"
                tabIndex={0}
                aria-label={readOut(panel)}
                onFocus={() => setActive({ month: panel.month, hour: 12 })}
                onBlur={() => setActive(null)}
                onMouseLeave={() => setActive(null)}
                onKeyDown={walk(panel)}
                className="outline-none"
              >
                {band && band.from !== null && band.to !== null && (
                  <rect data-band={band.label} x={0} y={yLine(band.to)} width={panelWidth} height={Math.max(0, yLine(band.from) - yLine(band.to))} fill={band.colour} opacity={0.3} />
                )}
                {panel.bars.map((value, hour) =>
                  value === undefined ? null : (
                    <rect
                      key={hour}
                      data-hour={hour}
                      x={xOf(hour)}
                      y={yBars(value)}
                      width={Math.max(0, barWidth - 1)}
                      height={Math.max(0, PANEL_HEIGHT - yBars(value))}
                      fill={BAR_FILL}
                      onMouseEnter={() => setActive({ month: panel.month, hour })}
                    />
                  ))}
                {points.length > 1 && (
                  <path data-series="line" d={`M${points.join('L')}`} fill="none" stroke={LINE_STROKE} strokeWidth={1.5} pointerEvents="none" />
                )}
                <text x={panelWidth / 2} y={PANEL_HEIGHT + LABEL_BAND - 3} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                  {panel.name}
                </text>
                {active?.month === panel.month && (
                  <rect x={xOf(active.hour)} y={0} width={Math.max(0, barWidth - 1)} height={PANEL_HEIGHT} fill="none" stroke="var(--chart-ink-muted)" strokeWidth={1} />
                )}
              </g>
            );
          })}
        </svg>
        {shown && active && (
          <ChartTooltip
            left={panelOrigin(panels.indexOf(shown)).left + xOf(active.hour)}
            top={panelOrigin(panels.indexOf(shown)).top}
            width={width}
            title={`${shown.name} ${String(active.hour).padStart(2, '0')}:00`}
          >
            <div className="flex items-baseline gap-2">
              <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: BAR_FILL }} aria-hidden="true" />
              <span className="font-semibold tabular-nums">{figure('bars', shown.bars[active.hour])}</span>
              <span className="text-zinc-500 dark:text-zinc-400">{widget.bars.label}</span>
            </div>
            <div className="flex items-baseline gap-2">
              <LineKey />
              <span className="font-semibold tabular-nums">{figure('line', shown.line[active.hour])}</span>
              <span className="text-zinc-500 dark:text-zinc-400">{widget.line.label}</span>
            </div>
          </ChartTooltip>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
        <li className="inline-flex items-center gap-1.5">
          <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: BAR_FILL }} aria-hidden="true" />
          {widget.bars.label}
        </li>
        <li className="inline-flex items-center gap-1.5">
          <LineKey />
          {widget.line.label}
        </li>
        {band && (
          <li className="inline-flex items-center gap-1.5">
            <i className="inline-block h-2.5 w-2.5 rounded-sm opacity-40" style={{ background: band.colour }} aria-hidden="true" />
            {band.label}
          </li>
        )}
      </ul>
      <SeriesTable
        caption={widget.title ?? ''}
        columns={[widget.bars.label, widget.line.label]}
        rows={panels.flatMap((panel) =>
          Array.from({ length: HOURS }, (_, hour) => ({
            name: `${panel.name} ${String(hour).padStart(2, '0')}:00`,
            cells: [figure('bars', panel.bars[hour]), figure('line', panel.line[hour])],
          })))}
      />
    </WidgetCard>
  );
}

function LineKey() {
  return (
    <svg width={12} height={12} aria-hidden="true">
      <line x1={0} x2={12} y1={6} y2={6} stroke={LINE_STROKE} strokeWidth={2} />
    </svg>
  );
}
