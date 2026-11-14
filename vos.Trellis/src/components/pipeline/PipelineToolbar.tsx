import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { Play, Save, FilePlus, Ban, History, Undo2, ArrowLeftRight } from 'lucide-react';
import type { RunInformation } from '../../pipeline/model';

interface PipelineToolbarProperties {
  name: string;
  onNameChange: (name: string) => void;
  canUndo: boolean;
  dirty: boolean;
  busy: boolean;
  nodeCount: number;
  savedId: string | null;
  findings: { message: string }[];
  runActive: boolean;
  runs: RunInformation[];
  runId: string | null;
  chosenRunRequest: string | undefined;
  error: string | null;
  onNew: () => void;
  onUndo: () => void;
  onSave: () => void;
  onRun: () => void;
  onCancel: () => void;
  onSelectRun: (runId: string) => void;
}

export function PipelineToolbar({
  name, onNameChange, canUndo, dirty, busy, nodeCount, savedId, findings, runActive, runs, runId, chosenRunRequest, error,
  onNew, onUndo, onSave, onRun, onCancel, onSelectRun,
}: PipelineToolbarProperties) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-wrap items-center gap-2 p-2 border-b border-zinc-200 dark:border-zinc-700">
      <button onClick={() => onNew()} className="flex items-center gap-1 px-3 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 hover:border-blue-400">
        <FilePlus size={14} /> {t('pipeline.new')}
      </button>
      <button
        onClick={onUndo}
        disabled={!canUndo}
        title={t('pipeline.undoTitle')}
        aria-label={t('pipeline.undo')}
        className="flex items-center gap-1 px-3 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 hover:border-blue-400 disabled:opacity-40"
      >
        <Undo2 size={14} /> {t('pipeline.undo')}
      </button>
      <input
        value={name}
        onChange={(e) => onNameChange(e.target.value)}
        aria-label={t('pipeline.name')}
        className="px-2 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
      />
      <button onClick={onSave} disabled={busy || nodeCount === 0} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-blue-600 text-white disabled:opacity-50">
        <Save size={14} /> {t('pipeline.save')}
      </button>
      {runActive ? (
        <button onClick={onCancel} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-amber-600 text-white">
          <Ban size={14} /> {t('pipeline.cancel')}
        </button>
      ) : (
        <button
          onClick={onRun}
          disabled={busy || !savedId || findings.length > 0}
          title={findings.length > 0 ? findings.map((i) => i.message).join('\n') : undefined}
          className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-green-600 text-white disabled:opacity-50"
        >
          <Play size={14} /> {t('pipeline.run')}
        </button>
      )}
      {savedId && runs.length > 0 && (
        <div className="flex items-center gap-1 ml-2">
          <History size={14} className="text-zinc-400" />
          <select
            onChange={(e) => onSelectRun(e.target.value)}
            value={runId && runs.some((r) => r.runId === runId) ? runId : ''}
            aria-label={t('pipeline.runHistory')}
            className="px-2 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
          >
            <option value="">{t('pipeline.history')}</option>
            {runs.map((r) => (
              <option key={r.runId} value={r.runId}>
                {r.status || t('pipeline.runLabel')}{r.startedUtc ? ` · ${r.startedUtc.slice(11, 19)}` : ''}
              </option>
            ))}
          </select>
          {chosenRunRequest && (
            <Link
              to={`/requests?entry=${encodeURIComponent(chosenRunRequest)}`}
              title={t('pipeline.openRequestTitle')}
              className="flex items-center gap-1 px-2 py-1 text-sm rounded text-blue-600 hover:underline"
            >
              <ArrowLeftRight size={14} /> {t('pipeline.openRequest')}
            </Link>
          )}
        </div>
      )}
      {dirty ? (
        <span className="text-xs text-amber-600">{t('pipeline.unsavedChanges')}</span>
      ) : savedId ? (
        <span className="text-xs text-green-600">{t('pipeline.saved')}</span>
      ) : null}
      {error && <span className="text-xs text-red-500 ml-2">{error}</span>}
    </div>
  );
}
