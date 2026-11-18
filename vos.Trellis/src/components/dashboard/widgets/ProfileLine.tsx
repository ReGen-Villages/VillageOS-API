import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ProfileLineWidget, ProfilePoint, ResolveContext } from '@tendril-ui/tendril';
import { profileFigures, profilePoints, profileSpanOf, profileWords, useBindings } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, SeriesTable } from './chartChrome';
import { linearScale, niceTicks, paddedDomain } from './chartScale';
import { extentOf } from './gridGeometry';
import { formatNumber } from './format';

const FALLBACK_WIDTH = 480;
const PLOT_HEIGHT = 200;
const TOP_GUTTER = 8;
const AXIS_BAND = 22;
const LEFT_GUTTER = 48;
const RIGHT_GUTTER = 8;
const TICKS = 5;
const POINT_RADIUS = 3;

/** The known heights as unbroken runs, so a height that is not known leaves a gap in the line. */
function runsOf(points: ProfilePoint[]): ProfilePoint[][] {
  const runs: ProfilePoint[][] = [];
  let run: ProfilePoint[] = [];
  for (const point of points) {
    if (point.height === null) {
      if (run.length > 0) runs.push(run);
      run = [];
    } else {
      run.push(point);
    }
  }
  if (run.length > 0) runs.push(run);
  return runs;
}

export function ProfileLine({ widget, context }: { widget: ProfileLineWidget; context: ResolveContext }) {
  const { t, i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<ProfilePoint | null>(null);
  const [heldProfile, heldFrom, heldTo] = useBindings([widget.profile, widget.span?.from, widget.span?.to], context);

  const points = useMemo(() => profilePoints(heldProfile.value), [heldProfile.value]);
  const span = widget.span ? profileSpanOf(heldFrom.value, heldTo.value) : null;
  const figures = points ? profileFigures(points, span) : null;
  const worded = figures ? profileWords(figures, widget.format, widget.unit, i18n.language) : null;

  const width = measuredWidth || FALLBACK_WIDTH;
  const known = (points ?? []).flatMap((point) => (point.height === null ? [] : [point.height]));
  const heights = extentOf(known);
  const [floor, ceiling] = heights ? paddedDomain([heights.lowest, heights.highest]) : [0, 1];
  const furthest = points && points.length > 1 ? points[points.length - 1].distanceMetres : 1;
  const x = linearScale([0, furthest], [LEFT_GUTTER, width - RIGHT_GUTTER]);
  const y = linearScale([floor, ceiling], [TOP_GUTTER + PLOT_HEIGHT, TOP_GUTTER]);
  const height = TOP_GUTTER + PLOT_HEIGHT + AXIS_BAND;
  const figure = (value: number) => `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;
  const distance = (metres: number) => `${formatNumber(metres, 'integer')} m`;

  if (!points) {
    return (
      <WidgetCard title={widget.title} hint={widget.hint}>
        <p className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">{heldProfile.value === null ? '' : t('widgets.profileLine.notAProfile')}</p>
      </WidgetCard>
    );
  }

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      <div ref={measure} className="relative mt-2">
        <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={[widget.title, worded && t('widgets.profileLine.figures', worded)].filter(Boolean).join('. ')} className="block max-w-full">
          {span && (
            <rect
              data-span
              x={x(span.from)}
              y={TOP_GUTTER}
              width={Math.max(0, x(span.to) - x(span.from))}
              height={PLOT_HEIGHT}
              fill="var(--chart-grid)"
              opacity={0.6}
            />
          )}
          {niceTicks(floor, ceiling, TICKS).filter((tick) => tick >= floor && tick <= ceiling).map((tick) => (
            <g key={`height-${tick}`}>
              <line x1={LEFT_GUTTER} x2={width - RIGHT_GUTTER} y1={y(tick)} y2={y(tick)} stroke="var(--chart-grid)" strokeWidth={1} />
              <text x={LEFT_GUTTER - 6} y={y(tick) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
                {formatNumber(tick)}
              </text>
            </g>
          ))}
          {niceTicks(0, furthest, TICKS).filter((tick) => tick <= furthest).map((tick) => (
            <text key={`distance-${tick}`} x={x(tick)} y={TOP_GUTTER + PLOT_HEIGHT + 14} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)" className="tabular-nums">
              {distance(tick)}
            </text>
          ))}
          {runsOf(points).map((run) => (
            <path
              key={run[0].distanceMetres}
              data-profile
              d={run.map((point, at) => `${at === 0 ? 'M' : 'L'}${x(point.distanceMetres).toFixed(2)},${y(point.height!).toFixed(2)}`).join(' ')}
              fill="none"
              stroke="var(--chart-ink)"
              strokeWidth={1.5}
            />
          ))}
          {points.map((point) => point.height === null ? null : (
            <circle
              key={point.distanceMetres}
              data-distance={point.distanceMetres}
              cx={x(point.distanceMetres)}
              cy={y(point.height)}
              r={POINT_RADIUS}
              fill="var(--chart-ink)"
              fillOpacity={active === point ? 1 : 0}
              onMouseEnter={() => setActive(point)}
              onMouseLeave={() => setActive(null)}
            />
          ))}
        </svg>
        {active && active.height !== null && (
          <ChartTooltip left={x(active.distanceMetres)} top={y(active.height)} width={width} title={t('widgets.profileLine.along', { distance: distance(active.distanceMetres) })}>
            <span className="font-semibold tabular-nums">{figure(active.height)}</span>
          </ChartTooltip>
        )}
      </div>
      {worded && (
        <p className="mt-1 text-[11px] text-zinc-500 dark:text-zinc-400 tabular-nums">{t('widgets.profileLine.figures', worded)}</p>
      )}
      <SeriesTable
        caption={widget.title ?? ''}
        columns={[t('widgets.profileLine.height')]}
        rows={points.map((point) => ({ name: distance(point.distanceMetres), cells: [point.height === null ? '—' : figure(point.height)] }))}
      />
    </WidgetCard>
  );
}
