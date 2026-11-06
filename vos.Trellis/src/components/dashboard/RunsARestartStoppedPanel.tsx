import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { RotateCcw, StepForward } from 'lucide-react';
import { formatRelativeTime } from '../../utils/formatters';
import type { RunARestartStopped } from '../../pipeline/model';

interface Props {
  runs: RunARestartStopped[];
  mayChangeData: boolean;
  /** Each answers whether the orchestrator accepted the ask. */
  onStartAgain: (runId: string) => Promise<boolean>;
  onCarryOn: (runId: string) => Promise<boolean>;
}

export function RunsARestartStoppedPanel({ runs, mayChangeData, onStartAgain, onCarryOn }: Props) {
  const { t } = useTranslation();
  const [asked, setAsked] = useState<ReadonlySet<string>>(new Set());

  // The run leaves the list only once the model's change reaches the page, so an accepted ask keeps the buttons off
  // until then, rather than offering an ask the orchestrator would now refuse.
  const ask = async (runId: string, asking: (runId: string) => Promise<boolean>) => {
    setAsked((previous) => new Set(previous).add(runId));
    if (await asking(runId)) return;
    setAsked((previous) => {
      const next = new Set(previous);
      next.delete(runId);
      return next;
    });
  };

  if (runs.length === 0) return null;

  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-amber-300 dark:border-amber-700 p-4">
      <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400 mb-1">{t('dashboard.restartStopped.title')}</h3>
      <p className="text-xs text-zinc-500 mb-3">{t('dashboard.restartStopped.explanation')}</p>
      <ul className="space-y-3">
        {runs.map((run) => (
          <li key={run.runId} className="p-3 bg-zinc-50 dark:bg-zinc-900 rounded-md border border-zinc-200 dark:border-zinc-700">
            <div className="flex flex-wrap items-center justify-between gap-2 mb-1">
              <span className="font-medium text-sm truncate">{run.pipelineName}</span>
              <span className="text-xs text-zinc-500">{t('dashboard.restartStopped.started', { time: formatRelativeTime(run.startedUtc) })}</span>
            </div>
            <p className="text-xs text-zinc-600 dark:text-zinc-400 break-words">{run.reason}</p>
            {mayChangeData && (
              <div className="flex flex-wrap gap-2 mt-2">
                <button
                  onClick={() => void ask(run.runId, onStartAgain)}
                  disabled={asked.has(run.runId)}
                  title={t('dashboard.restartStopped.startAgainHint')}
                  className="flex items-center gap-1 px-2 py-1 text-xs rounded border border-zinc-300 dark:border-zinc-600 hover:bg-zinc-100 dark:hover:bg-zinc-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <RotateCcw size={12} />
                  {t('dashboard.restartStopped.startAgain')}
                </button>
                <button
                  onClick={() => void ask(run.runId, onCarryOn)}
                  disabled={asked.has(run.runId)}
                  title={t('dashboard.restartStopped.carryOnHint')}
                  className="flex items-center gap-1 px-2 py-1 text-xs rounded border border-zinc-300 dark:border-zinc-600 hover:bg-zinc-100 dark:hover:bg-zinc-700 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  <StepForward size={12} />
                  {t('dashboard.restartStopped.carryOn')}
                </button>
              </div>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
