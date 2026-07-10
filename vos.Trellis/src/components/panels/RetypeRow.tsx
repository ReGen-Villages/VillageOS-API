import { useState, useMemo, useCallback } from 'react';
import { ThingPicker } from '../common/ThingPicker';
import { retypeThing } from '../../utils/retype';
import { toast } from '../common/Toast';
import type { VosThing, VosRelationship } from '../../types/vos';

// Retype control (#5860): change one of a Thing's types by repointing an is-edge. Multiple inheritance is
// preserved — when the Thing has more than one type, a "from" picker chooses which one to replace. Thin
// glue over the tested retypeThing helper; the built-in `is` predicate is resolved from the model by name.
interface Props {
  thingId: string;
  things: VosThing[];
  relationships: VosRelationship[];
  onDone?: () => void;
}

export function RetypeRow({ thingId, things, relationships, onDone }: Props) {
  const [toId, setToId] = useState('');
  const [fromId, setFromId] = useState('');
  const [saving, setSaving] = useState(false);

  const isPredicateId = useMemo(
    () => things.find((t) => t.Name.toLowerCase() === 'is')?.Id,
    [things],
  );

  const currentTypeIds = useMemo(
    () =>
      relationships
        .filter((r) => r.SubjectId === thingId && r.PredicateId === isPredicateId)
        .map((r) => r.TargetId),
    [relationships, thingId, isPredicateId],
  );
  const currentTypeThings = useMemo(
    () => things.filter((t) => currentTypeIds.includes(t.Id)),
    [things, currentTypeIds],
  );
  const needsFrom = currentTypeIds.length > 1;

  const submit = useCallback(async () => {
    if (!toId || !isPredicateId || (needsFrom && !fromId) || saving) return;
    setSaving(true);
    try {
      await retypeThing(thingId, toId, isPredicateId, relationships, needsFrom ? fromId : undefined);
      toast.success('Retyped');
      setToId('');
      setFromId('');
      onDone?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to retype');
    } finally {
      setSaving(false);
    }
  }, [toId, isPredicateId, needsFrom, fromId, saving, thingId, relationships, onDone]);

  // Nothing to retype against if the model has no `is` predicate Thing.
  if (!isPredicateId) return null;

  return (
    <div className="border-t border-zinc-700/50 mt-2 pt-2 space-y-1.5">
      <div className="text-xs text-zinc-400">Retype — change a type (is-edge):</div>
      {needsFrom && (
        <ThingPicker things={currentTypeThings} value={fromId} onChange={setFromId} placeholder="Replace which type..." />
      )}
      <ThingPicker things={things} value={toId} onChange={setToId} placeholder="New archetype..." />
      <div className="flex justify-end">
        <button
          onClick={submit}
          disabled={!toId || (needsFrom && !fromId) || saving}
          title="Retype"
          className="px-2 py-0.5 rounded text-xs text-blue-400 hover:bg-blue-500/20 disabled:opacity-30 disabled:cursor-default"
        >
          Retype
        </button>
      </div>
    </div>
  );
}
