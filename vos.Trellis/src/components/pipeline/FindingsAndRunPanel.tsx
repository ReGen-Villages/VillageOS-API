import { useTranslation } from 'react-i18next';
import type { Node } from '@xyflow/react';
import clsx from 'clsx';
import { AlertTriangle } from 'lucide-react';
import type { PipelineNodeData } from './PipelineNodeView';

const RUN_STATUS_COLOR: Record<string, string> = {
  running: 'text-blue-600',
  waiting: 'text-amber-600',
  succeeded: 'text-green-600',
  failed: 'text-red-500',
  refused: 'text-red-500',
  cancelled: 'text-amber-600',
  partial: 'text-orange-500',
};

interface FindingsAndRunPanelProperties {
  findings: { message: string }[];
  runId: string | null;
  liveRunStatus: string | undefined;
  runActive: boolean;
  nodes: Node[];
}

export function FindingsAndRunPanel({ findings, runId, liveRunStatus, runActive, nodes }: FindingsAndRunPanelProperties) {
  const { t } = useTranslation();
  if (findings.length === 0 && !runId) return null;
  return (
    <div className="border-t border-zinc-200 dark:border-zinc-700 p-2 text-xs max-h-40 overflow-auto">
      {findings.length > 0 && (
        <ul aria-label={t('pipeline.findings')} className="mb-1 space-y-0.5">
          {findings.map((issue, i) => (
            <li key={i} className="flex items-start gap-1 text-amber-700 dark:text-amber-400">
              <AlertTriangle size={12} className="mt-0.5 shrink-0" /> <span>{issue.message}</span>
            </li>
          ))}
        </ul>
      )}
      {runId && (
        <>
          <div className={clsx('font-semibold', RUN_STATUS_COLOR[liveRunStatus ?? 'running'] ?? 'text-blue-600')}>
            {t('pipeline.runStatus', { status: liveRunStatus ?? t('pipeline.starting') })}{runActive ? '…' : ''}
          </div>
          {nodes.map((n) => {
            const d = n.data as unknown as PipelineNodeData;
            return (
              <div key={n.id} className="font-mono">
                {t('pipeline.nodeStatus', { label: d.label, status: d.status ?? t('pipeline.pending') })}
              </div>
            );
          })}
        </>
      )}
    </div>
  );
}
