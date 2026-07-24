import { useState, useMemo, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
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
  const { t } = useTranslation();
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
      toast.success(t('panels.retype.retyped'));
      setToId('');
      setFromId('');
      onDone?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('panels.retype.retypeFailed'));
    } finally {
      setSaving(false);
    }
  }, [toId, isPredicateId, needsFrom, fromId, saving, thingId, relationships, onDone, t]);

  // Nothing to retype against if the model has no `is` predicate Thing.
  if (!isPredicateId) return null;

  return (
    <div className="border-t border-zinc-700/50 mt-2 pt-2 space-y-1.5">
      <div className="text-xs text-zinc-400">{t('panels.retype.heading')}</div>
      {needsFrom && (
        <ThingPicker things={currentTypeThings} value={fromId} onChange={setFromId} placeholder={t('panels.retype.replaceWhich')} />
      )}
      <ThingPicker things={things} value={toId} onChange={setToId} placeholder={t('panels.retype.newArchetype')} />
      <div className="flex justify-end">
        <button
          onClick={submit}
          disabled={!toId || (needsFrom && !fromId) || saving}
          title={t('panels.retype.retype')}
          className="px-2 py-0.5 rounded text-xs text-blue-400 hover:bg-blue-500/20 disabled:opacity-30 disabled:cursor-default"
        >
          {t('panels.retype.retype')}
        </button>
      </div>
    </div>
  );
}
