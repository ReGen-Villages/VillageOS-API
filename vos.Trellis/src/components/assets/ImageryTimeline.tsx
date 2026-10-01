import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AssetImage } from './AssetImage';
import { assetTicketOf } from '../../utils/assetTicket';
import { formatDateTime } from '../../utils/formatters';
import type { PropertyVersion } from '../../types/vos';

interface Props {
  versions: PropertyVersion[];
}

interface ImageryVersion {
  ticket: string;
  timestamp: string;
}

/**
 * A property history whose values are asset tickets, as the imagery it names: a slider scrubs
 * across the series — every point shows the exact bytes that were true then, because assets are
 * immutable — and one instant can be pinned so another can be scrubbed in beside it. Versions
 * that are not tickets stay in the plain list; a series carrying none renders nothing here.
 */
export function ImageryTimeline({ versions }: Props) {
  const { t } = useTranslation();
  const imagery: ImageryVersion[] = versions
    .flatMap((version) => {
      const ticket = assetTicketOf(version.Value);
      return ticket ? [{ ticket, timestamp: version.Timestamp }] : [];
    })
    .sort((left, right) => left.timestamp.localeCompare(right.timestamp));

  const [selectedIndex, setSelectedIndex] = useState(imagery.length - 1);
  const [pinnedIndex, setPinnedIndex] = useState<number | null>(null);

  if (imagery.length === 0) return null;

  const shownIndex = Math.min(selectedIndex, imagery.length - 1);
  const shown = imagery[shownIndex];
  const pinned =
    pinnedIndex !== null && pinnedIndex !== shownIndex && pinnedIndex < imagery.length
      ? imagery[pinnedIndex]
      : null;

  return (
    <div className="space-y-2 mb-3">
      <div className="text-xs font-semibold text-zinc-500">{t('assets.timeline')}</div>
      <input
        type="range"
        min={0}
        max={imagery.length - 1}
        value={shownIndex}
        onChange={(event) => setSelectedIndex(Number(event.target.value))}
        aria-label={t('assets.scrub')}
        className="w-full accent-blue-500"
      />
      <div className="flex flex-wrap items-end gap-4">
        {pinned && (
          <figure className="space-y-1 opacity-80">
            <AssetImage
              ticket={pinned.ticket}
              className="max-h-48 rounded border border-zinc-300 dark:border-zinc-600"
            />
            <figcaption className="text-xs font-mono text-zinc-500">
              {`${t('assets.pinned')} ${formatDateTime(pinned.timestamp)}`}
            </figcaption>
          </figure>
        )}
        <figure className="space-y-1">
          <AssetImage
            ticket={shown.ticket}
            className="max-h-48 rounded border border-zinc-200 dark:border-zinc-700"
          />
          <figcaption className="text-xs font-mono text-zinc-400">
            {formatDateTime(shown.timestamp)}
          </figcaption>
        </figure>
      </div>
      <button
        onClick={() => setPinnedIndex(pinnedIndex === null ? shownIndex : null)}
        className="px-2 py-1 text-xs rounded-md border border-zinc-300 dark:border-zinc-600 text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-700"
      >
        {pinnedIndex === null ? t('assets.pin') : t('assets.unpin')}
      </button>
    </div>
  );
}
