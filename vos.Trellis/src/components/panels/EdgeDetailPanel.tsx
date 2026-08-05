import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { X, Copy, Trash2, Pencil } from 'lucide-react';
import type { VosRelationship, VosThing, ThingRangesResponse, ThingStates } from '../../types/vos';
import { formatGuid } from '../../utils/formatters';
import { toast } from '../common/Toast';
import { EditablePropertyList, withDeclaredTypes } from './EditablePropertyList';
import { RangesTabContent } from './RangesTabContent';
import { relationshipRangeApi } from '../../api/rangeApi';
import { useResolvedRelationshipProperties } from '../../hooks/useResolvedRelationshipProperties';

interface Props {
  relationship: VosRelationship;
  allThings: Map<string, VosThing>;
  onClose: () => void;
  onSelectNode: (id: string) => void;
  onDeleteRelationship: (id: string) => void;
  onDeleteProperty?: (relationshipId: string, propertyName: string) => void;
  onPropertySet?: () => void;
  statesVersion?: number;
}

export function EdgeDetailPanel({ relationship: rel, allThings, onClose, onSelectNode, onDeleteRelationship, onDeleteProperty, onPropertySet, statesVersion }: Props) {
  const { t } = useTranslation();
  const subject = allThings.get(rel.SubjectId);
  const predicate = allThings.get(rel.PredicateId);
  const target = allThings.get(rel.TargetId);
  const [editMode, setEditMode] = useState(false);
  const [tab, setTab] = useState<'properties' | 'ranges'>('ranges');
  const [rangesData, setRangesData] = useState<ThingRangesResponse | null>(null);
  const [statesData, setStatesData] = useState<ThingStates | null>(null);
  const [rangesLoading, setRangesLoading] = useState(false);
  const [propertiesVersion, setPropertiesVersion] = useState(0);
  const resolvedProperties = useResolvedRelationshipProperties(rel.Id, {
    version: propertiesVersion,
    enabled: tab === 'properties',
  });
  const ownProperties = rel.Properties ? Object.entries(rel.Properties) : [];
  const ownPropertyCount = ownProperties.length;
  const props = withDeclaredTypes(ownProperties, resolvedProperties);

  const handlePropertySaved = () => {
    setPropertiesVersion((v) => v + 1);
    onPropertySet?.();
  };

  // Fetch ranges and states lazily when ranges tab is active
  useEffect(() => {
    if (tab !== 'ranges') return;
    let cancelled = false;
    setRangesLoading(true);
    (async () => {
      try {
        const [rangesResp, statesResp] = await Promise.all([
          relationshipRangeApi.getAll(rel.Id),
          relationshipRangeApi.getStates(rel.Id),
        ]);
        if (!cancelled) {
          // Adapt to ThingRangesResponse shape (no inherited ranges for relationships)
          setRangesData({
            ThingId: rangesResp.RelationshipId,
            ThingName: rangesResp.RelationshipName,
            OwnRanges: rangesResp.OwnRanges,
            InheritedRanges: [],
          });
          setStatesData({
            ThingId: statesResp.RelationshipId,
            ThingName: statesResp.RelationshipName,
            CurrentStates: statesResp.CurrentStates,
            RangeEvaluations: statesResp.RangeEvaluations,
            OutOfBoundsCount: statesResp.OutOfBoundsCount,
          });
        }
      } catch {
        if (!cancelled) { setRangesData(null); setStatesData(null); }
      } finally {
        if (!cancelled) setRangesLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [rel.Id, tab, statesVersion]);

  const copyId = () => {
    navigator.clipboard.writeText(rel.Id);
    toast.info(t('panels.node.idCopied'));
  };

  const tabs: Array<{ key: typeof tab; label: string }> = [
    { key: 'ranges', label: t('panels.edge.rangesTab') },
    { key: 'properties', label: t('panels.edge.propertiesTab', { count: ownPropertyCount }) },
  ];

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="p-3 border-b border-zinc-200 dark:border-zinc-700 flex items-center justify-between">
        <div className="min-w-0">
          <h3 className="font-semibold text-sm truncate">{rel.Name}</h3>
          <button onClick={copyId} className="flex items-center gap-1 text-xs text-zinc-500 hover:text-zinc-300">
            <Copy size={10} /> {formatGuid(rel.Id)}
          </button>
        </div>
        <button onClick={onClose} className="text-zinc-400 hover:text-zinc-200">
          <X size={16} />
        </button>
      </div>

      <div className="flex-1 overflow-auto p-3 text-sm space-y-4">
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <span className="text-xs text-zinc-500">{t('panels.edge.subject')}</span>
            <button onClick={() => subject && onSelectNode(subject.Id)} className="text-xs text-emerald-400 hover:underline">
              {subject?.Name || formatGuid(rel.SubjectId)}
            </button>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-xs text-zinc-500">{t('panels.edge.predicate')}</span>
            <button onClick={() => predicate && onSelectNode(predicate.Id)} className="text-xs text-amber-400 hover:underline">
              {predicate?.Name || formatGuid(rel.PredicateId)}
            </button>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-xs text-zinc-500">{t('panels.edge.target')}</span>
            <button onClick={() => target && onSelectNode(target.Id)} className="text-xs text-blue-400 hover:underline">
              {target?.Name || formatGuid(rel.TargetId)}
            </button>
          </div>
        </div>

        <div className="flex gap-1 border-b border-zinc-700">
          {tabs.map((t) => (
            <button
              key={t.key}
              onClick={() => setTab(t.key)}
              className={`px-2 py-1 text-xs font-medium border-b-2 transition-colors ${
                tab === t.key
                  ? 'border-blue-500 text-blue-400'
                  : 'border-transparent text-zinc-500 hover:text-zinc-300'
              }`}
            >
              {t.label}
            </button>
          ))}
        </div>

        {tab === 'properties' && (
          <div>
            <div className="flex items-center justify-end mb-1">
              <button
                onClick={() => setEditMode((v) => !v)}
                disabled={!resolvedProperties}
                className={`p-0.5 rounded transition-colors disabled:opacity-30 disabled:cursor-default ${
                  editMode
                    ? 'text-blue-400 bg-blue-500/20 hover:bg-blue-500/30'
                    : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
                }`}
                title={editMode ? t('panels.node.exitEditMode') : t('panels.node.editProperties')}
              >
                <Pencil size={12} />
              </button>
            </div>
            <EditablePropertyList
              properties={props}
              entityId={rel.Id}
              entityType="relationship"
              editMode={editMode}
              onSaved={handlePropertySaved}
              onDeleteProperty={onDeleteProperty ? (name) => onDeleteProperty(rel.Id, name) : undefined}
            />
          </div>
        )}

        {tab === 'ranges' && (
          <RangesTabContent
            rangesData={rangesData}
            statesData={statesData}
            loading={rangesLoading}
            onSelectNode={onSelectNode}
          />
        )}
      </div>

      <div className="p-2 border-t border-zinc-200 dark:border-zinc-700">
        <button
          onClick={() => onDeleteRelationship(rel.Id)}
          className="w-full text-center py-1.5 text-xs text-red-400 hover:bg-red-900/20 rounded"
        >
          <Trash2 size={12} className="inline mr-1" /> {t('panels.edge.deleteRelationship')}
        </button>
      </div>
    </div>
  );
}
