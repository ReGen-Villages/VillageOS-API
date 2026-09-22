import { useState } from 'react';
import clsx from 'clsx';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import { usePanelWidth, type PanelWidthBounds } from '../../hooks/usePanelWidth';

interface Props {
  /** Which side of the work the panel stands on. Everything that differs between the two —
   *  which side the handle is on, which way the chevron points, which border is drawn — follows
   *  from this, so a caller never states the same fact twice. */
  side: 'left' | 'right';
  bounds: Omit<PanelWidthBounds, 'handleSide'>;
  labels: { expand: string; collapse: string; resize: string };
  /** What to call this panel, drawn at its head and announced as its region. Required, because a
   *  page carrying three of these holds three lists, and three called nothing are three a reader
   *  cannot tell apart. */
  name: string;
  /** Controls that belong beside the fold control rather than in the body — a close button, say.
   *  Hidden with the body while the panel is folded, because a rail has no room for them. */
  headerControls?: React.ReactNode;
  children: React.ReactNode;
}

/**
 * A panel beside the canvas, which folds to a rail when the canvas wants the room and drags wider
 * when what it holds needs more than it has.
 *
 * Both are working positions rather than settings, so nothing outside the panel remembers either
 * and every page opens on the width it was designed for. The fold control lives on the rail it
 * leaves behind — a panel that folded away and took the way back with it would be gone for good.
 */
export function SidePanel({ side, bounds, labels, name, headerControls, children }: Props) {
  const [isFolded, setIsFolded] = useState(false);
  const panel = usePanelWidth({ ...bounds, handleSide: side === 'left' ? 'right' : 'left' });

  const pointsAway = side === 'left' ? isFolded : !isFolded;
  const Chevron = pointsAway ? ChevronRight : ChevronLeft;

  const heading = !isFolded && (
    <h2 className="flex-1 min-w-0 truncate px-1.5 text-xs font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
      {name}
    </h2>
  );

  // Narrow and unpainted until it is wanted: a permanent bar between every panel would draw more
  // attention than the flow it sits next to.
  const handle = !isFolded && (
    <div
      {...panel.handleProperties}
      aria-label={labels.resize}
      className={clsx(
        'w-1.5 flex-shrink-0 cursor-col-resize transition-colors',
        panel.isResizing ? 'bg-blue-500/60' : 'bg-transparent hover:bg-blue-400/40',
      )}
    />
  );

  return (
    <>
      {side === 'right' && handle}
      <aside
        aria-label={name}
        style={isFolded ? undefined : { width: panel.width }}
        className={clsx(
          'flex-shrink-0 flex flex-col border-zinc-200 dark:border-zinc-700',
          side === 'left' ? 'border-r' : 'border-l',
          // Folded, it is the width of the control that opens it again and nothing else.
          isFolded && 'w-9',
        )}
      >
        <div className="flex items-center gap-1 p-1.5 border-b border-zinc-200 dark:border-zinc-700">
          {side === 'left' && heading}
          <button
            type="button"
            onClick={() => setIsFolded(!isFolded)}
            aria-expanded={!isFolded}
            aria-label={isFolded ? labels.expand : labels.collapse}
            className="rounded p-1 text-zinc-500 dark:text-zinc-400 hover:bg-zinc-100 dark:hover:bg-zinc-800"
          >
            <Chevron size={15} />
          </button>
          {side === 'right' && heading}
          {!isFolded && headerControls}
        </div>

        {!isFolded && <div className="flex-1 min-h-0 flex flex-col overflow-hidden">{children}</div>}
      </aside>
      {side === 'left' && handle}
    </>
  );
}
