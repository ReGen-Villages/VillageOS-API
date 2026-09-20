import { useTranslation } from 'react-i18next';
import { X } from 'lucide-react';

/** One item of a list — a column, a hop, a stage, a relation — boxed under its own name, with the
 *  control that takes it away. */
export function Removable({ name, onRemove, children }: { name: string; onRemove: () => void; children: React.ReactNode }) {
  const { t } = useTranslation();
  return (
    <div className="rounded-md border border-zinc-200 dark:border-zinc-700 p-2 space-y-2">
      <div className="flex items-center justify-between font-mono text-[10px] text-zinc-500 dark:text-zinc-400">
        <span>{name}</span>
        <button type="button" aria-label={`${t('design.list.remove')} ${name}`} onClick={onRemove} className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200">
          <X size={12} />
        </button>
      </div>
      {children}
    </div>
  );
}
