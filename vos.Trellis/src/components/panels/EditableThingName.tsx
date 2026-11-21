import { useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Pencil } from 'lucide-react';
import { thingApi } from '../../api/thingApi';
import { toast } from '../common/toastStore';

// A blank or unchanged name sends nothing. `onRenamed` lets the parent show the new name at once; the broker's
// own update arrives on the stream as well.
interface Props {
  thingId: string;
  name: string;
  onRenamed?: (newName: string) => void;
}

export function EditableThingName({ thingId, name, onRenamed }: Props) {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(name);
  const [saving, setSaving] = useState(false);

  const begin = useCallback(() => {
    setDraft(name);
    setEditing(true);
  }, [name]);

  const commit = useCallback(async () => {
    const next = draft.trim();
    if (saving) return;
    if (!next || next === name) {
      setEditing(false);
      return;
    }
    setSaving(true);
    try {
      await thingApi.rename(thingId, next);
      toast.success(t('panels.name.renamed'));
      onRenamed?.(next);
      setEditing(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('panels.name.renameFailed'));
    } finally {
      setSaving(false);
    }
  }, [draft, name, saving, thingId, onRenamed, t]);

  if (editing) {
    return (
      <input
        aria-label={t('panels.name.thingName')}
        autoFocus
        value={draft}
        disabled={saving}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === 'Enter') commit();
          else if (e.key === 'Escape') setEditing(false);
        }}
        className="w-full bg-transparent border-b border-blue-400 text-sm font-semibold outline-none"
      />
    );
  }

  return (
    <div className="flex items-center gap-1 min-w-0">
      <h3 className="font-semibold text-sm truncate">{name}</h3>
      <button
        onClick={begin}
        title={t('panels.name.rename')}
        aria-label={t('panels.name.rename')}
        className="p-0.5 text-zinc-400 hover:text-blue-400 transition-colors shrink-0"
      >
        <Pencil size={12} />
      </button>
    </div>
  );
}
