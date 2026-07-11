import { useState, useCallback } from 'react';
import { Pencil } from 'lucide-react';
import { thingApi } from '../../api/thingApi';
import { toast } from '../common/Toast';

// Inline rename affordance for the node detail panel header (#5862). Click the pencil to edit the name in
// place; Enter (or blur) commits via thingApi.rename — which keeps the Thing's Id and all edges — and Escape
// cancels. A blank or unchanged name is a no-op (no broker call). The optional onRenamed lets the parent
// reflect the new name immediately; the authoritative update also arrives over SSE.
interface Props {
  thingId: string;
  name: string;
  onRenamed?: (newName: string) => void;
}

export function EditableThingName({ thingId, name, onRenamed }: Props) {
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
      toast.success('Renamed');
      onRenamed?.(next);
      setEditing(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to rename');
    } finally {
      setSaving(false);
    }
  }, [draft, name, saving, thingId, onRenamed]);

  if (editing) {
    return (
      <input
        aria-label="Thing name"
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
        title="Rename"
        aria-label="Rename"
        className="p-0.5 text-zinc-400 hover:text-blue-400 transition-colors shrink-0"
      >
        <Pencil size={12} />
      </button>
    </div>
  );
}
