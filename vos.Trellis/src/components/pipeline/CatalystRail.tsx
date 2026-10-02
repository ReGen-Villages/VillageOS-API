import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Plus } from 'lucide-react';
import type { CatalystGroup, CatalystKind, CatalystRow } from '../../pipeline/catalysts';
import { SidePanel } from '../common/SidePanel';

interface Props {
  groups: [CatalystGroup, CatalystGroup];
  /** Place a start node standing for the row's Thing on the open drawing; undefined for a start by hand. */
  onPlace: (row: CatalystRow | undefined) => void;
  onOpenPipeline: (pipelineId: string) => void;
  /** Bind the row's state to the orchestrator, so the pipeline drawn from it is what runs. */
  onHandOver: (row: CatalystRow) => void;
}

const RAIL_BOUNDS = { initial: 232, min: 168, max: 480 };
const HEADING = 'px-3 py-2 text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400';

const KIND_LABEL_KEY = {
  message: 'pipeline.catalysts.kind.message',
  state: 'pipeline.catalysts.kind.state',
  relationship: 'pipeline.catalysts.kind.relationship',
  clock: 'pipeline.catalysts.kind.clock',
} as const satisfies Record<CatalystKind, string>;

/** Everything that sets a run off, read off the model: what arrives from outside, and what the model
 *  does on its own. Each row says which pipeline is drawn for it, or what happens to it today, and
 *  places a start node standing for it. The first row is a start by hand, which stands for nothing. */
export function CatalystRail({ groups, onPlace, onOpenPipeline, onHandOver }: Props) {
  const { t } = useTranslation();

  const words = (row: CatalystRow) => {
    const who = row.who || (row.kind === 'message' ? t('pipeline.catalysts.anybody') : '');
    const what = row.everySeconds === undefined ? row.what : `${row.what} · ${t('pipeline.catalysts.everySeconds', { count: row.everySeconds })}`;
    return who ? `${who} · ${what}` : what;
  };

  // A state says it starts its drawing only when the orchestrator receives its entries; sent anywhere
  // else, the drawing never runs, so the row names both the drawing and what really happens.
  const outcomeLine = (row: CatalystRow) => {
    const today = row.today ? t('pipeline.catalysts.today', { service: row.today }) : t('pipeline.catalysts.nothingToday');
    const runsTheDrawing = row.starts !== undefined && (row.handling === undefined || row.handling.byOrchestrator);
    const text = runsTheDrawing ? t('pipeline.catalysts.starts', { pipeline: row.starts!.name })
      : row.handling?.byOrchestrator ? t('pipeline.catalysts.orchestratorRefuses')
      : row.starts ? t('pipeline.catalysts.drawn', { pipeline: row.starts.name, today })
      : today;
    return (
      <span className={clsx('block text-[10px]', runsTheDrawing ? 'text-emerald-700 dark:text-emerald-400' : 'text-zinc-400')}>
        {text}
      </span>
    );
  };

  // The kind is said once for each run of rows of that kind, not on every row: twenty doors in a row
  // are twenty rows under one heading.
  const rowView = (row: CatalystRow, index: number, rows: CatalystRow[]) => (
    <li key={row.id} className="rounded-md px-2 py-1.5 hover:bg-zinc-100 dark:hover:bg-zinc-800">
      {(index === 0 || rows[index - 1].kind !== row.kind) && (
        <span className="block text-[10px] uppercase tracking-wide text-zinc-400">{t(KIND_LABEL_KEY[row.kind])}</span>
      )}
      <div className="flex items-start gap-1">
      <div className="flex-1 min-w-0">
        {row.starts ? (
          <button
            type="button"
            onClick={() => onOpenPipeline(row.starts!.id)}
            title={t('pipeline.catalysts.openTitle', { pipeline: row.starts.name })}
            className="block w-full text-left break-words text-[12px] text-zinc-700 dark:text-zinc-300 hover:underline"
          >
            {words(row)}
          </button>
        ) : (
          <span className="block break-words text-[12px] text-zinc-700 dark:text-zinc-300">{words(row)}</span>
        )}
        {outcomeLine(row)}
        {row.offersOrchestrator && (
          <button
            type="button"
            onClick={() => onHandOver(row)}
            aria-label={t('pipeline.catalysts.handOverTitle', { what: words(row) })}
            title={t('pipeline.catalysts.handOverTitle', { what: words(row) })}
            className="block text-[10px] text-blue-600 dark:text-blue-400 hover:underline"
          >
            {t('pipeline.catalysts.handOver')}
          </button>
        )}
      </div>
      <button
        type="button"
        onClick={() => onPlace(row)}
        aria-label={t('pipeline.catalysts.placeTitle', { what: words(row) })}
        title={t('pipeline.catalysts.placeTitle', { what: words(row) })}
        className="mt-1 shrink-0 rounded p-0.5 text-zinc-400 hover:text-blue-500"
      >
        <Plus size={13} />
      </button>
      </div>
    </li>
  );

  return (
    <SidePanel
      side="left"
      bounds={RAIL_BOUNDS}
      name={t('pipeline.catalysts.title')}
      labels={{ expand: t('pipeline.catalysts.expand'), collapse: t('pipeline.catalysts.collapse'), resize: t('pipeline.catalysts.resize') }}
    >
      <div className="flex-1 overflow-y-auto">
        <ul className="p-1">
          <li className="flex items-start gap-1 rounded-md px-2 py-1.5 hover:bg-zinc-100 dark:hover:bg-zinc-800">
            <div className="flex-1 min-w-0">
              <span className="block text-[12px] text-zinc-700 dark:text-zinc-300">{t('pipeline.catalysts.byHand')}</span>
              <span className="block text-[10px] text-zinc-400">{t('pipeline.catalysts.byHandBody')}</span>
            </div>
            <button
              type="button"
              onClick={() => onPlace(undefined)}
              aria-label={t('pipeline.catalysts.placeByHand')}
              title={t('pipeline.catalysts.placeByHand')}
              className="mt-1 shrink-0 rounded p-0.5 text-zinc-400 hover:text-blue-500"
            >
              <Plus size={13} />
            </button>
          </li>
        </ul>
        {groups.map((group) => (
          <section key={group.side} aria-label={t(group.side === 'external' ? 'pipeline.catalysts.external' : 'pipeline.catalysts.internal')}>
            <h3 className={clsx(HEADING, 'border-t border-zinc-200 dark:border-zinc-700')}>
              {t(group.side === 'external' ? 'pipeline.catalysts.external' : 'pipeline.catalysts.internal')}
            </h3>
            <ul className="p-1 pt-0">
              {group.rows.length === 0 && <li className="px-2 text-xs text-zinc-400">{t('pipeline.catalysts.none')}</li>}
              {group.rows.map(rowView)}
            </ul>
          </section>
        ))}
      </div>
    </SidePanel>
  );
}
