import { useTranslation } from 'react-i18next';
import type { ConnectionInfo } from '../../pipeline/model';

/** The node palette: boundary Input/Output nodes (#5873) plus every dispatchable Connection in the model.
 * Click a service to drop a node bound to it, or a boundary node for the pipeline's external input/output. */
export function Palette({
  connections,
  onAdd,
  onAddBoundary,
}: {
  connections: ConnectionInfo[];
  onAdd: (c: ConnectionInfo) => void;
  onAddBoundary: (kind: 'input' | 'output') => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="w-56 flex-shrink-0 border-r border-zinc-200 dark:border-zinc-700 bg-zinc-50 dark:bg-zinc-900 overflow-auto">
      <h2 className="px-3 py-2 text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
        {t('palette.boundary')}
      </h2>
      <div className="grid grid-cols-2 gap-1 p-2 pt-0">
        <button
          onClick={() => onAddBoundary('input')}
          title={t('palette.inputTitle')}
          className="rounded-md border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-800 px-2 py-1.5 text-sm font-medium hover:border-green-400 transition-colors"
        >
          + {t('palette.input')}
        </button>
        <button
          onClick={() => onAddBoundary('output')}
          title={t('palette.outputTitle')}
          className="rounded-md border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-800 px-2 py-1.5 text-sm font-medium hover:border-blue-400 transition-colors"
        >
          + {t('palette.output')}
        </button>
      </div>
      <h2 className="px-3 py-2 text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
        {t('palette.services')}
      </h2>
      {connections.length === 0 && (
        <p className="px-3 text-xs text-zinc-400">{t('palette.noConnections')}</p>
      )}
      <div className="space-y-1 p-2 pt-0">
        {connections.map((c) => (
          <button
            key={c.connectionId}
            onClick={() => onAdd(c)}
            className="w-full text-left rounded-md border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-800 px-2.5 py-1.5 hover:border-blue-400 transition-colors"
          >
            <div className="text-sm font-medium text-zinc-800 dark:text-zinc-100 truncate">{c.name}</div>
            <div className="text-[10px] font-mono text-zinc-400">
              {c.subdomain} · {t('palette.portCount', { count: c.ports.length })}
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
