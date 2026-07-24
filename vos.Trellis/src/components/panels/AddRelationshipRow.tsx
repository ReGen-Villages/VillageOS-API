import { useState, useMemo, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Loader2 } from 'lucide-react';
import { ThingPicker } from '../common/ThingPicker';
import { relationshipApi } from '../../api/relationshipApi';
import { toast } from '../common/Toast';
import type { VosThing, VosRelationship } from '../../types/vos';

interface Props {
  direction: 'outgoing' | 'incoming';
  fixedThingId: string;
  things: VosThing[];
  relationships: VosRelationship[];
  onCreated?: () => void;
}

export function AddRelationshipRow({ direction, fixedThingId, things, relationships, onCreated }: Props) {
  const { t } = useTranslation();
  const [predicateId, setPredicateId] = useState('');
  const [otherId, setOtherId] = useState('');
  const [saving, setSaving] = useState(false);

  const { nonPredicateThings, predicateSorted } = useMemo(() => {
    const predicateIds = new Set(relationships.map((r) => r.PredicateId));
    const known = things.filter((t) => predicateIds.has(t.Id));
    const rest = things.filter((t) => !predicateIds.has(t.Id));
    return {
      nonPredicateThings: rest,
      predicateSorted: [...known, ...rest],
    };
  }, [things, relationships]);

  const canSubmit = predicateId && otherId && !saving;

  const submit = useCallback(async () => {
    if (!canSubmit) return;
    setSaving(true);
    try {
      const subjectId = direction === 'outgoing' ? fixedThingId : otherId;
      const targetId = direction === 'outgoing' ? otherId : fixedThingId;
      await relationshipApi.create(subjectId, predicateId, targetId);
      toast.success(t('panels.addRel.created'));
      setPredicateId('');
      setOtherId('');
      onCreated?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('panels.addRel.createFailed'));
    } finally {
      setSaving(false);
    }
  }, [canSubmit, direction, fixedThingId, otherId, predicateId, onCreated, t]);

  return (
    <div className="border-t border-zinc-700/50 mt-2 pt-2 space-y-1.5">
      <ThingPicker things={predicateSorted} value={predicateId} onChange={setPredicateId} placeholder={t('panels.addRel.predicatePlaceholder')} />
      <ThingPicker things={nonPredicateThings} value={otherId} onChange={setOtherId} placeholder={direction === 'outgoing' ? t('panels.addRel.targetPlaceholder') : t('panels.addRel.subjectPlaceholder')} />
      <div className="flex justify-end">
        <button
          onClick={submit}
          disabled={!canSubmit}
          className="p-0.5 rounded text-emerald-400 hover:bg-emerald-500/20 disabled:opacity-30 disabled:cursor-default transition-colors flex-shrink-0"
          title={t('panels.addRel.createRelationship')}
        >
          {saving ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />}
        </button>
      </div>
    </div>
  );
}
