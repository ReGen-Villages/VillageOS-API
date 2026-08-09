import { useTranslation } from 'react-i18next';
import type { EngineMetricsSummary } from '../../types/engineMetrics';
import { formatBytes } from '../../utils/formatters';

interface Props {
  /** Null until the first load succeeds — the panel then says so instead of showing zeros
   *  that would read as a healthy empty engine. */
  metrics: EngineMetricsSummary | null;
}

/** Compact capacity card for the two reactive engines: reactor counts, dependency fan-in, and the
 *  estimated memory each engine carries. Drill-in to per-reactor detail is Taproot's (#5856). */
export function EngineMetricsPanel({ metrics }: Props) {
  const { t } = useTranslation();

  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400 mb-3">
        {t('dashboard.engines.title')}
      </h3>
      {metrics === null ? (
        <p className="text-xs text-zinc-400">{t('dashboard.engines.unavailable')}</p>
      ) : (
        <>
          <EngineRow
            label={t('dashboard.engines.ranges')}
            reactors={metrics.Ranges.RegisteredRanges}
            reactorsUnit={t('dashboard.engines.rangesUnit')}
            fanIn={metrics.Ranges.DependencyEdges}
            fanInUnit={t('dashboard.engines.edges')}
            bytes={metrics.Ranges.EstimatedBytes}
          />
          <EngineRow
            label={t('dashboard.engines.rollups')}
            reactors={metrics.Rollups.RollupProperties}
            reactorsUnit={t('dashboard.engines.rollupsUnit')}
            fanIn={metrics.Rollups.MemberEdges}
            fanInUnit={t('dashboard.engines.members')}
            bytes={metrics.Rollups.EstimatedBytes}
          />
          <div className="flex justify-between mt-2 pt-2 border-t border-zinc-200 dark:border-zinc-700 text-xs">
            <span className="text-zinc-500 dark:text-zinc-400">{t('dashboard.engines.total')}</span>
            <span className="font-mono font-semibold">{formatBytes(metrics.EstimatedBytesTotal)}</span>
          </div>
        </>
      )}
    </div>
  );
}

function EngineRow({
  label,
  reactors,
  reactorsUnit,
  fanIn,
  fanInUnit,
  bytes,
}: {
  label: string;
  reactors: number;
  reactorsUnit: string;
  fanIn: number;
  fanInUnit: string;
  bytes: number;
}) {
  return (
    <div className="flex items-baseline justify-between py-1 text-xs">
      <span className="text-zinc-500 dark:text-zinc-400">{label}</span>
      <span className="flex gap-3 font-mono">
        <span>
          <span className="font-semibold">{reactors}</span>{' '}
          <span className="text-zinc-400">{reactorsUnit}</span>
        </span>
        <span>
          <span className="font-semibold">{fanIn}</span>{' '}
          <span className="text-zinc-400">{fanInUnit}</span>
        </span>
        <span>{formatBytes(bytes)}</span>
      </span>
    </div>
  );
}
