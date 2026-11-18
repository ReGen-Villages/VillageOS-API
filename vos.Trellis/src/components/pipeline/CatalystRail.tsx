import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Plus } from 'lucide-react';
import type { CatalystGroup, CatalystGroupName, CatalystRow } from '../../pipeline/catalysts';
import { SidePanel } from '../common/SidePanel';

interface Props {
  groups: CatalystGroup[];
  /** Place a start node standing for the row's Thing on the open drawing; undefined for a start by hand. */
  onPlace: (row: CatalystRow | undefined) => void;
  onOpenPipeline: (pipelineId: string) => void;
  /** Bind the row's state to the orchestrator, so the pipeline drawn from it is what runs. */
  onHandOver: (row: CatalystRow) => void;
  onShowSettings: (connectionId: string) => void;
}

const RAIL_BOUNDS = { initial: 232, min: 168, max: 480 };
const HEADING = 'px-3 py-2 text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400';

const GROUP_HEADING_KEY = {
  messages: 'pipeline.catalysts.group.messages',
  states: 'pipeline.catalysts.group.states',
  clock: 'pipeline.catalysts.group.clock',
  relationships: 'pipeline.catalysts.group.relationships',
} as const satisfies Record<CatalystGroupName, string>;

/** Everything that sets a run off, read off the model, in its groups. Each row says what the model does with it
 *  today, a drawing, a service or nothing, and places a start node standing for it where a run may start from one.
 *  The first row is a start by hand, which stands for nothing. */
export function CatalystRail({ groups, onPlace, onOpenPipeline, onHandOver, onShowSettings }: Props) {
  const { t } = useTranslation();

  const words = (row: CatalystRow) => {
    const who = row.who || (row.kind === 'message' ? t('pipeline.catalysts.anybody') : '');
    const every = row.everySeconds !== undefined ? t('pipeline.catalysts.everySeconds', { count: row.everySeconds })
      : row.everyMinutes !== undefined ? t('pipeline.catalysts.everyMinutes', { count: row.everyMinutes })
      : undefined;
    const what = every ? `${row.what} · ${every}` : row.what;
    return who ? `${who} · ${what}` : what;
  };

  const handlingCell = (row: CatalystRow) => {
    const { handledBy } = row;
    const [text, title] = handledBy.by === 'drawing' ? [handledBy.name, t('pipeline.catalysts.starts', { pipeline: handledBy.name })]
      : handledBy.by === 'service' ? [handledBy.name, t('pipeline.catalysts.today', { service: handledBy.name })]
      : [t('pipeline.catalysts.nothing'),
        t(handledBy.refusedByTheOrchestrator ? 'pipeline.catalysts.orchestratorRefuses' : 'pipeline.catalysts.nothingToday')];
    return (
      <span
        title={title}
        className={clsx('mt-0.5 max-w-[45%] shrink-0 truncate text-right text-[10px]',
          handledBy.by === 'drawing' ? 'text-emerald-700 dark:text-emerald-400'
          : handledBy.by === 'service' ? 'text-zinc-500 dark:text-zinc-400' : 'text-zinc-400 italic dark:text-zinc-500')}
      >
        {text}
      </span>
    );
  };

  const rowView = (row: CatalystRow) => (
    <li key={row.id} className="rounded-md px-2 py-1.5 hover:bg-zinc-100 dark:hover:bg-zinc-800">
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
        {row.handling && (
          <button
            type="button"
            onClick={() => onShowSettings(row.handling!.connectionId)}
            aria-label={t('pipeline.catalysts.settingsTitle', { what: words(row) })}
            title={t('pipeline.catalysts.settingsTitle', { what: words(row) })}
            className="block text-[10px] text-blue-600 dark:text-blue-400 hover:underline"
          >
            {t('pipeline.catalysts.settings')}
          </button>
        )}
      </div>
      {handlingCell(row)}
      {row.placeable && (
        <button
          type="button"
          onClick={() => onPlace(row)}
          aria-label={t('pipeline.catalysts.placeTitle', { what: words(row) })}
          title={t('pipeline.catalysts.placeTitle', { what: words(row) })}
          className="mt-1 shrink-0 rounded p-0.5 text-zinc-400 hover:text-blue-500"
        >
          <Plus size={13} />
        </button>
      )}
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
          <section key={group.name} aria-label={t(GROUP_HEADING_KEY[group.name])}>
            <h3 className={clsx(HEADING, 'border-t border-zinc-200 dark:border-zinc-700')}>
              {t(GROUP_HEADING_KEY[group.name])}
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
