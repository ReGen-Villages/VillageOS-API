import { useState, useMemo, useCallback } from 'react';
import { ThingPicker } from '../common/ThingPicker';
import { retypeThing } from '../../utils/retype';
import { toast } from '../common/Toast';
import type { VosThing, VosRelationship } from '../../types/vos';

// Retype control (#5860): pick a new archetype and repoint the Thing's is-edge to it. Thin glue over the
// tested retypeThing helper; resolves the built-in `is` predicate from the loaded model by name.
interface Props {
  thingId: string;
  things: VosThing[];
  relationships: VosRelationship[];
  onDone?: () => void;
}

export function RetypeRow({ thingId, things, relationships, onDone }: Props) {
  const [archetypeId, setArchetypeId] = useState('');
  const [saving, setSaving] = useState(false);

  const isPredicateId = useMemo(
    () => things.find((t) => t.Name.toLowerCase() === 'is')?.Id,
    [things],
  );

  const submit = useCallback(async () => {
    if (!archetypeId || !isPredicateId || saving) return;
    setSaving(true);
    try {
      await retypeThing(thingId, archetypeId, isPredicateId, relationships);
      toast.success('Retyped');
      setArchetypeId('');
      onDone?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to retype');
    } finally {
      setSaving(false);
    }
  }, [archetypeId, isPredicateId, saving, thingId, relationships, onDone]);

  // Nothing to retype against if the model has no `is` predicate Thing.
  if (!isPredicateId) return null;

  return (
    <div className="border-t border-zinc-700/50 mt-2 pt-2 space-y-1.5">
      <div className="text-xs text-zinc-400">Retype — change the is-edge:</div>
      <ThingPicker things={things} value={archetypeId} onChange={setArchetypeId} placeholder="New archetype..." />
      <div className="flex justify-end">
        <button
          onClick={submit}
          disabled={!archetypeId || saving}
          title="Retype"
          className="px-2 py-0.5 rounded text-xs text-blue-400 hover:bg-blue-500/20 disabled:opacity-30 disabled:cursor-default"
        >
          Retype
        </button>
      </div>
    </div>
  );
}
