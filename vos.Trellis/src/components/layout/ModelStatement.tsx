import clsx from 'clsx';
import { useTranslation } from 'react-i18next';
import { useSse } from '../../hooks/useSse';
import { useModelStore } from '../../stores/modelStore';
import { useActivityStore } from '../../stores/activityStore';

/** A line restating what is on screen. A console is left open for hours and read by whoever walks
 *  past it, so it has to say how much the model holds, whether changes are still arriving and how
 *  long ago anything moved, without being asked. */
export function ModelStatement({ isCollapsed }: { isCollapsed: boolean }) {
  const { t, i18n } = useTranslation();
  const { connected } = useSse();
  const loaded = useModelStore((s) => s.loaded);
  const thingCount = useModelStore((s) => s.things.length);
  const relationshipCount = useModelStore((s) => s.relationships.length);
  const latestEvent = useActivityStore((s) => s.events[s.events.length - 1]);

  const liveness = connected ? t('statement.live') : t('statement.notLive');
  const held = loaded
    ? `${t('statement.things', { count: thingCount })} · ${t('statement.relationships', { count: relationshipCount })}`
    : t('statement.reading');
  const moved = latestEvent
    ? t('statement.lastMoved', { at: new Date(latestEvent.Timestamp).toLocaleTimeString(i18n.language) })
    : t('statement.nothingYet');

  const mark = (
    <span
      className={clsx('inline-block w-1.5 h-1.5 rounded-full flex-shrink-0', connected ? 'bg-blue-600 dark:bg-blue-400' : 'bg-zinc-400 dark:bg-zinc-600')}
      aria-hidden="true"
    />
  );

  if (isCollapsed) {
    return (
      <div className="flex justify-center py-1" title={`${liveness} · ${held} · ${moved}`}>
        {mark}
      </div>
    );
  }

  return (
    <div className="px-1 py-1 text-xs text-zinc-500 dark:text-zinc-400 space-y-0.5">
      <div className="flex items-center gap-1.5">
        {mark}
        <span>{liveness}</span>
      </div>
      <div className="truncate">{held}</div>
      <div className="truncate">{moved}</div>
    </div>
  );
}
