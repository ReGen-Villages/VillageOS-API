import { Trash2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';

/** Only starts a deletion; the caller confirms it and retracts. */
export function DeleteThingButton({ onDelete }: { onDelete: () => void }) {
  const { t } = useTranslation();
  return (
    <button
      onClick={onDelete}
      title={t('panels.delete.deleteRetract')}
      className="p-1 text-zinc-400 hover:text-red-400 transition-colors"
    >
      <Trash2 size={14} />
    </button>
  );
}
