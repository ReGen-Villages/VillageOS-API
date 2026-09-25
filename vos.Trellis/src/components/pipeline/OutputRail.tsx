import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import type { OutputRow } from '../../pipeline/catalysts';
import type { ConnectionInformation } from '../../pipeline/model';
import { SidePanel } from '../common/SidePanel';

interface Props {
  outputs: OutputRow[];
  connections: ConnectionInformation[];
  /** Place an end node standing for the row's Thing, or for nothing where the row is the answer. */
  onPlaceOutput: (row: OutputRow) => void;
  onAddService: (connection: ConnectionInformation) => void;
}

const RAIL_BOUNDS = { initial: 232, min: 168, max: 480 };
const HEADING = 'px-3 py-2 text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400';
const ROW = 'w-full text-left rounded-md border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-800 px-2.5 py-1.5 transition-colors';

/** What a run may leave behind — the answer, another pipeline, an external system told — and under it
 *  every service a node may dispatch, read from the connections and ports the model declares. */
export function OutputRail({ outputs, connections, onPlaceOutput, onAddService }: Props) {
  const { t } = useTranslation();
  const answer = outputs.find((row) => row.kind === 'answer');
  const pipelines = outputs.filter((row) => row.kind === 'pipeline');
  const systems = outputs.filter((row) => row.kind === 'externalSystem');

  const placeButton = (row: OutputRow, label: string, body: string | undefined, hover: string) => (
    <li key={row.id}>
      <button
        type="button"
        onClick={() => onPlaceOutput(row)}
        aria-label={t('pipeline.outputs.placeTitle', { what: label })}
        title={t('pipeline.outputs.placeTitle', { what: label })}
        className={`${ROW} ${hover}`}
      >
        <span className="flex items-center gap-1">
          <Plus size={12} className="shrink-0 text-zinc-400" />
          <span className="text-sm font-medium text-zinc-800 dark:text-zinc-100 truncate">{label}</span>
        </span>
        {body && <span className="block text-[10px] text-zinc-500 dark:text-zinc-400 break-words">{body}</span>}
      </button>
    </li>
  );

  return (
    <SidePanel
      side="right"
      bounds={RAIL_BOUNDS}
      name={t('pipeline.outputs.title')}
      labels={{ expand: t('pipeline.outputs.expand'), collapse: t('pipeline.outputs.collapse'), resize: t('pipeline.outputs.resize') }}
    >
      <div className="flex-1 overflow-y-auto">
        <ul className="space-y-1 p-2">
          {answer && placeButton(answer, t('pipeline.outputs.answer'), t('pipeline.outputs.answerBody'), 'hover:border-blue-400')}
        </ul>
        {pipelines.length > 0 && (
          <>
            <h3 className={HEADING}>{t('pipeline.outputs.pipelines')}</h3>
            <ul className="space-y-1 p-2 pt-0">
              {pipelines.map((row) => placeButton(row, row.name, undefined, 'hover:border-emerald-400'))}
            </ul>
          </>
        )}
        {systems.length > 0 && (
          <>
            <h3 className={HEADING}>{t('pipeline.outputs.systems')}</h3>
            <ul className="space-y-1 p-2 pt-0">
              {systems.map((row) => placeButton(
                row, row.name,
                row.told && row.told.length > 0 ? t('pipeline.outputs.told', { kinds: row.told.join(', ') }) : t('pipeline.outputs.toldNothing'),
                'hover:border-violet-400',
              ))}
            </ul>
          </>
        )}
        <h3 className={`${HEADING} border-t border-zinc-200 dark:border-zinc-700`}>{t('pipeline.outputs.services')}</h3>
        {connections.length === 0 && <p className="px-3 text-xs text-zinc-400">{t('pipeline.outputs.noConnections')}</p>}
        <ul aria-label={t('pipeline.outputs.services')} className="space-y-1 p-2 pt-0">
          {connections.map((connection) => (
            <li key={connection.connectionId}>
              <button type="button" onClick={() => onAddService(connection)} className={`${ROW} hover:border-blue-400`}>
                <span className="block text-sm font-medium text-zinc-800 dark:text-zinc-100 truncate">{connection.name}</span>
                <span className="block text-[10px] font-mono text-zinc-400">
                  {connection.subdomain} · {t('pipeline.outputs.portCount', { count: connection.ports.length })}
                </span>
              </button>
            </li>
          ))}
        </ul>
      </div>
    </SidePanel>
  );
}
