import { useTranslation } from 'react-i18next';
import { GripVertical } from 'lucide-react';
import type { Widget } from '../../types/dashboard';
import { WIDGET_KINDS } from '../../utils/gridLayout';

interface Props {
  onLift: (kind: Widget['type']) => (event: React.DragEvent) => void;
  onSettle: () => void;
  onAdd: (kind: Widget['type']) => void;
}

/** Every kind of widget the renderer draws, dragged onto a section or clicked to land in the
 *  selected one. The list is the renderer's own, so a kind added there appears here unasked. */
export function DesignPalette({ onLift, onSettle, onAdd }: Props) {
  const { t } = useTranslation();

  return (
    <aside className="w-40 flex-shrink-0 flex flex-col border-r border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900">
      <h2 className="px-3 pt-3 pb-1 text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{t('design.palette.title')}</h2>
      <div className="flex-1 p-2 space-y-1.5 overflow-y-auto">
        {WIDGET_KINDS.map((kind) => {
          const label = t(`design.palette.kind.${kind}`);
          return (
            <button
              key={kind}
              type="button"
              draggable
              aria-label={t('design.palette.add', { kind: label })}
              title={t('design.palette.dragHint')}
              onClick={() => onAdd(kind)}
              onDragStart={onLift(kind)}
              onDragEnd={onSettle}
              className="flex w-full items-start gap-1.5 rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 px-2 py-1.5 text-left text-xs text-zinc-700 dark:text-zinc-200 cursor-grab active:cursor-grabbing"
            >
              <GripVertical size={12} className="mt-0.5 text-zinc-400 flex-shrink-0" />
              <span className="min-w-0 break-words">{label}</span>
            </button>
          );
        })}
      </div>
    </aside>
  );
}
