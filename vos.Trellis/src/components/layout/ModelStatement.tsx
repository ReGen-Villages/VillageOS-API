import { useTranslation } from 'react-i18next';
import { useSse } from '../../hooks/useSse';
import { useModelStore } from '../../stores/modelStore';
import { useActivityStore } from '../../stores/activityStore';
import { ConnectionMark } from '../common/ConnectionMark';

/** A line restating what is on screen. A console is left open for hours and read by whoever walks
 *  past it, so it has to say how much the model holds, whether changes are still arriving and how
 *  long ago anything moved, without being asked. Where the open page holds only its own set of
 *  Things the count is given as the page's: the size of the model is not known there. */
export function ModelStatement({ isCollapsed }: { isCollapsed: boolean }) {
  const { t, i18n } = useTranslation();
  const { connection } = useSse();
  const loaded = useModelStore((s) => s.loaded);
  const holdsWholeModel = useModelStore((s) => s.holdsWholeModel);
  const thingCount = useModelStore((s) => s.things.length);
  const relationshipCount = useModelStore((s) => s.relationships.length);
  const latestEvent = useActivityStore((s) => s.events[s.events.length - 1]);

  const liveness = t(`connection.${connection}`);
  const counted = `${t('statement.things', { count: thingCount })} · ${t('statement.relationships', { count: relationshipCount })}`;
  const held = !loaded
    ? t('statement.reading')
    : holdsWholeModel ? counted : t('statement.onThisPage', { held: counted });
  const moved = latestEvent
    ? t('statement.lastMoved', { at: new Date(latestEvent.Timestamp).toLocaleTimeString(i18n.language) })
    : t('statement.nothingYet');

  const mark = <ConnectionMark state={connection} className="w-1.5 h-1.5" />;

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
