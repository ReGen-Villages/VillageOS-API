import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ThingRangesResponse, ThingStates, RangeDto, RangeEvaluation, InheritedRangeSetDto, PropertyBindingDto } from '../../types/vos';
import { Badge } from '../common/Badge';
import { WindmillSpinner } from '../common/WindmillSpinner';
import { stateColor, rangeBindingColor } from '../../utils/rangeHelpers';
import { rangeApi } from '../../api/rangeApi';
import { toast } from '../common/Toast';
import { Trash2, Plus, Check, X } from 'lucide-react';

/** A relationship's ranges + states, bundled for display in the thing's Ranges tab. */
export interface RelationshipRangesEntry {
  relationshipId: string;
  relationshipName: string;
  /** e.g. "Subject → Predicate → Target" */
  label: string;
  rangesData: ThingRangesResponse;
  statesData: ThingStates;
}

interface Props {
  rangesData: ThingRangesResponse | null;
  statesData: ThingStates | null;
  loading: boolean;
  onSelectNode: (id: string) => void;
  /** Ranges from the thing's relationships (optional). */
  relationshipRanges?: RelationshipRangesEntry[];
  /** The entity ID (thing or relationship) — required for range CRUD. */
  entityId?: string;
  /** Whether CRUD operations are enabled (only for things, not inherited). */
  editable?: boolean;
  /** Callback after a range is created or deleted. */
  onRangeChanged?: () => void;
}

export function RangesTabContent({ rangesData, statesData, loading, onSelectNode, relationshipRanges, entityId, editable, onRangeChanged }: Props) {
  const { t } = useTranslation();
  if (loading) return (
    <div className="flex items-center justify-center py-4">
      <WindmillSpinner size={24} />
    </div>
  );

  if (!statesData) return <p className="text-zinc-500 text-xs italic">{t('panels.ranges.noRangeData')}</p>;

  return (
    <>
      {editable && entityId && (
        <CreateRangeForm thingId={entityId} onCreated={onRangeChanged} />
      )}

      {statesData.OutOfBoundsCount > 0 && (
        <div className="flex items-center gap-2 px-2 py-1.5 rounded bg-red-500/10 border border-red-500/20">
          <span className="inline-block w-2 h-2 rounded-full bg-red-500 flex-shrink-0" />
          <span className="text-xs text-red-400">
            {t('panels.ranges.bindingsOutOfBounds', { count: statesData.OutOfBoundsCount })}
          </span>
        </div>
      )}

      <div>
        <h4 className="text-xs font-semibold text-zinc-500 mb-1">
          {t('panels.ranges.currentStates', { count: statesData.CurrentStates.length })}
        </h4>
        {statesData.CurrentStates.length === 0 ? (
          <p className="text-zinc-500 text-xs italic">{t('panels.ranges.noActiveStates')}</p>
        ) : (
          <div className="flex flex-wrap gap-1">
            {statesData.CurrentStates.map((s) => (
              <Badge key={s} label={s} color={stateColor(s, rangesData)} dot />
            ))}
          </div>
        )}
      </div>

      {rangesData && (
        <div>
          <h4 className="text-xs font-semibold text-zinc-500 mb-1">
            {t('panels.ranges.ownRanges', { count: rangesData.OwnRanges.length })}
          </h4>
          {rangesData.OwnRanges.length === 0 ? (
            <p className="text-zinc-500 text-xs italic">{t('panels.ranges.noOwnRanges')}</p>
          ) : (
            <div className="space-y-1">
              {rangesData.OwnRanges.map((r) => (
                <RangeItem
                  key={r.Name}
                  range={r}
                  evaluation={statesData.RangeEvaluations.find((e) => e.RangeName === r.Name)}
                  onDelete={editable && entityId ? async () => {
                    try {
                      await rangeApi.delete(entityId, r.Name);
                      toast.success(t('panels.ranges.rangeDeleted', { name: r.Name }));
                      onRangeChanged?.();
                    } catch (err) {
                      toast.error(err instanceof Error ? err.message : t('graph.toast.deleteFailed'));
                    }
                  } : undefined}
                />
              ))}
            </div>
          )}
        </div>
      )}

      {rangesData && rangesData.InheritedRanges.length > 0 && (
        <div>
          <h4 className="text-xs font-semibold text-zinc-500 mb-1">{t('panels.ranges.inheritedRanges')}</h4>
          {rangesData.InheritedRanges.map((irs) => (
            <InheritedRangeGroupView
              key={irs.SourceId}
              set={irs}
              evaluations={statesData.RangeEvaluations}
              onSelectNode={onSelectNode}
            />
          ))}
        </div>
      )}

      {relationshipRanges && relationshipRanges.length > 0 && (
        <div>
          <h4 className="text-xs font-semibold text-zinc-500 mb-1">
            {t('panels.ranges.relationshipRanges', { count: relationshipRanges.length })}
          </h4>
          <div className="space-y-2">
            {relationshipRanges.map((entry) => (
              <RelationshipRangesGroup key={entry.relationshipId} entry={entry} />
            ))}
          </div>
        </div>
      )}
    </>
  );
}

function RangeItem({ range, evaluation, onDelete }: { range: RangeDto; evaluation?: RangeEvaluation; onDelete?: () => void }) {
  const { t } = useTranslation();
  const hasDeviations = range.Bindings?.some((b) => b.IsActive && b.IsInBounds === false);
  return (
    <div className="py-1 border-b border-zinc-800 last:border-0">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium">{range.Name}</span>
        <div className="flex items-center gap-1">
          {onDelete && (
            <button
              onClick={onDelete}
              className="p-0.5 rounded text-zinc-600 hover:text-red-400 hover:bg-red-900/20 transition-colors"
              title={t('panels.ranges.deleteRange', { name: range.Name })}
            >
              <Trash2 size={10} />
            </button>
          )}
          {hasDeviations && <Badge label={t('panels.ranges.outOfBounds')} color="red" dot />}
          {!hasDeviations && range.ActiveBindings > 0 && (
            <span className="text-[10px] text-zinc-500">
              {t('panels.ranges.activeBindings', { count: range.ActiveBindings })}
            </span>
          )}
          {evaluation && (
            evaluation.Error
              ? <Badge label={t('panels.ranges.error')} color="red" />
              : <Badge label={evaluation.IsActive ? t('panels.ranges.active') : t('panels.ranges.inactive')} color={evaluation.IsActive ? 'green' : 'gray'} dot />
          )}
        </div>
      </div>
      <p className="text-[10px] font-mono text-zinc-500 mt-0.5 break-all">{range.Criteria}</p>
      {evaluation?.Error && (
        <p className="text-[10px] text-red-400 mt-0.5">{evaluation.Error}</p>
      )}
      {range.Bindings?.length > 0 && (
        <div className="mt-1 space-y-0.5">
          {range.Bindings.map((b) => (
            <BindingRow key={b.PropertyName} binding={b} />
          ))}
        </div>
      )}
    </div>
  );
}

function BindingRow({ binding: b }: { binding: PropertyBindingDto }) {
  const { t } = useTranslation();
  const outOfBounds = b.IsActive && b.IsInBounds === false;
  const dotColor = outOfBounds ? 'bg-red-500' : b.IsActive ? 'bg-green-500' : 'bg-zinc-600';

  return (
    <div className={`flex items-start gap-1.5 text-[10px] py-0.5 rounded ${outOfBounds ? 'bg-red-500/10' : ''}`}>
      <span className={`inline-block w-1.5 h-1.5 rounded-full flex-shrink-0 mt-[3px] ${dotColor}`} />
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-1 flex-wrap">
          <span className="font-medium text-zinc-300">{b.PropertyName}</span>
          <span className="font-mono text-zinc-500">{b.BoundsDescription}</span>
          {b.GuardCriteria && (
            <span className="text-zinc-600 italic">{t('panels.ranges.when', { criteria: b.GuardCriteria })}</span>
          )}
        </div>
        {b.IsActive && b.CurrentValue !== undefined && b.CurrentValue !== null && (
          <div className="flex items-center gap-1 mt-0.5">
            <span className={`font-mono ${outOfBounds ? 'text-red-400 font-semibold' : 'text-zinc-400'}`}>
              = {String(b.CurrentValue)}
            </span>
            {outOfBounds && b.DeviationDelta != null && (
              <span className="text-red-400/70">
                {t('panels.ranges.off', { delta: `${b.DeviationDelta > 0 ? '+' : ''}${Number(b.DeviationDelta).toFixed(1)}` })}
              </span>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function InheritedRangeGroupView({ set, evaluations, onSelectNode }: {
  set: InheritedRangeSetDto;
  evaluations: RangeEvaluation[];
  onSelectNode: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="pl-2 border-l-2 border-zinc-700 mb-2">
      <button
        onClick={() => onSelectNode(set.SourceId)}
        className="text-xs text-amber-400 hover:underline mb-1 block"
      >
        {t('panels.ranges.from', { source: set.SourceName })}
      </button>
      {set.Ranges.map((r) => (
        <RangeItem key={r.Name} range={r} evaluation={evaluations.find((e) => e.RangeName === r.Name)} />
      ))}
      {set.Inherited.map((nested) => (
        <InheritedRangeGroupView
          key={nested.SourceId}
          set={nested}
          evaluations={evaluations}
          onSelectNode={onSelectNode}
        />
      ))}
    </div>
  );
}

function CreateRangeForm({ thingId, onCreated }: { thingId: string; onCreated?: () => void }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [name, setName] = useState('');
  const [criteria, setCriteria] = useState('');
  const [saving, setSaving] = useState(false);
  const [validationError, setValidationError] = useState<string | null>(null);

  const validateAndSave = async () => {
    if (!name.trim() || !criteria.trim()) return;
    setSaving(true);
    setValidationError(null);
    try {
      const result = await rangeApi.validateCriteria(criteria);
      if (!result.IsValid) {
        setValidationError(result.Error || t('panels.ranges.invalidCriteria'));
        setSaving(false);
        return;
      }
      await rangeApi.create(thingId, { Name: name.trim(), Criteria: criteria.trim() });
      toast.success(t('panels.ranges.rangeCreated', { name }));
      setName('');
      setCriteria('');
      setOpen(false);
      onCreated?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('panels.ranges.createRangeFailed'));
    } finally {
      setSaving(false);
    }
  };

  if (!open) {
    return (
      <button
        onClick={() => setOpen(true)}
        className="flex items-center gap-1 text-xs text-blue-400 hover:text-blue-300 transition-colors"
      >
        <Plus size={12} /> {t('panels.ranges.addRange')}
      </button>
    );
  }

  return (
    <div className="space-y-2 p-2 rounded border border-zinc-700 bg-zinc-800/50">
      <div>
        <label className="block text-[10px] font-medium text-zinc-500 mb-0.5">{t('panels.ranges.name')}</label>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder={t('panels.ranges.namePlaceholder')}
          className="w-full px-2 py-1 text-xs rounded border border-zinc-600 bg-zinc-800 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
      </div>
      <div>
        <label className="block text-[10px] font-medium text-zinc-500 mb-0.5">{t('panels.ranges.criteria')}</label>
        <input
          value={criteria}
          onChange={(e) => { setCriteria(e.target.value); setValidationError(null); }}
          placeholder={t('panels.ranges.criteriaPlaceholder')}
          className="w-full px-2 py-1 text-xs font-mono rounded border border-zinc-600 bg-zinc-800 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
        {validationError && (
          <p className="text-[10px] text-red-400 mt-0.5">{validationError}</p>
        )}
      </div>
      <div className="flex items-center gap-1">
        <button
          onClick={validateAndSave}
          disabled={saving || !name.trim() || !criteria.trim()}
          className="flex items-center gap-1 px-2 py-0.5 text-xs rounded bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          <Check size={10} /> {saving ? t('common.saving') : t('panels.ranges.create')}
        </button>
        <button
          onClick={() => { setOpen(false); setName(''); setCriteria(''); setValidationError(null); }}
          className="flex items-center gap-1 px-2 py-0.5 text-xs rounded text-zinc-400 hover:text-zinc-200 hover:bg-zinc-700"
        >
          <X size={10} /> {t('common.cancel')}
        </button>
      </div>
    </div>
  );
}

function RelationshipRangesGroup({ entry }: { entry: RelationshipRangesEntry }) {
  const { t } = useTranslation();
  const { statesData, rangesData } = entry;
  const totalOob = statesData.OutOfBoundsCount;

  return (
    <div className="pl-2 border-l-2 border-blue-500/30 mb-2">
      <div className="flex items-center gap-1 mb-1">
        <span className="text-xs text-blue-400 font-medium truncate">{entry.label}</span>
        {totalOob > 0 && <Badge label={t('panels.ranges.oob', { count: totalOob })} color="red" dot />}
      </div>

      {statesData.CurrentStates.length > 0 && (
        <div className="flex flex-wrap gap-1 mb-1">
          {statesData.CurrentStates.map((s) => (
            <Badge key={s} label={s} color={rangeBindingColor(rangesData.OwnRanges.find((r) => r.Name === s))} dot />
          ))}
        </div>
      )}

      {rangesData.OwnRanges.map((r) => (
        <RangeItem
          key={r.Name}
          range={r}
          evaluation={statesData.RangeEvaluations.find((e) => e.RangeName === r.Name)}
        />
      ))}
    </div>
  );
}
