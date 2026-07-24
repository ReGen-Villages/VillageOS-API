import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronDown, ChevronRight, ExternalLink } from 'lucide-react';
import { EditablePropertyList } from './EditablePropertyList';
import { AddRelationshipRow } from './AddRelationshipRow';
import type { VosRelationship, VosThing } from '../../types/vos';
import { formatGuid } from '../../utils/formatters';

interface Props {
  relationships: VosRelationship[];
  direction: 'outgoing' | 'incoming';
  allThings: Map<string, VosThing>;
  onSelectNode: (id: string) => void;
  onSelectEdge?: (id: string) => void;
  editMode?: boolean;
  onPropertySaved?: () => void;
  fixedThingId?: string;
  allRelationships?: VosRelationship[];
  onRelationshipCreated?: () => void;
}

export function RelationshipList({ relationships, direction, allThings, onSelectNode, onSelectEdge, editMode = false, onPropertySaved, fixedThingId, allRelationships, onRelationshipCreated }: Props) {
  const { t } = useTranslation();
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());

  return (
    <div>
      <h4 className="text-xs font-semibold text-zinc-500 mb-1">
        {direction === 'outgoing' ? t('panels.rel.outgoing') : t('panels.rel.incoming')} ({relationships.length})
      </h4>
      {relationships.map((r) => {
        const pred = allThings.get(r.PredicateId);
        const other = allThings.get(direction === 'outgoing' ? r.TargetId : r.SubjectId);
        const otherId = direction === 'outgoing' ? r.TargetId : r.SubjectId;
        const relProps = r.Properties ? Object.entries(r.Properties) : [];
        const isExpanded = expandedIds.has(r.Id);

        return (
          <div key={r.Id}>
            <div className="flex items-center py-1 text-xs hover:bg-zinc-200 dark:hover:bg-zinc-800 rounded px-1">
              {relProps.length > 0 && (
                <button
                  onClick={() => setExpandedIds((prev) => {
                    const next = new Set(prev);
                    if (next.has(r.Id)) next.delete(r.Id); else next.add(r.Id);
                    return next;
                  })}
                  className="text-zinc-500 hover:text-zinc-300 mr-1 shrink-0"
                >
                  {isExpanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
                </button>
              )}
              {direction === 'incoming' && (
                <button onClick={() => onSelectNode(otherId)} className="text-emerald-400 hover:underline truncate">
                  {other?.Name || formatGuid(otherId)}
                </button>
              )}
              {direction === 'incoming' && <span className="mx-1">{' → '}</span>}
              <span className="text-blue-400">{pred?.Name || formatGuid(r.PredicateId)}</span>
              {direction === 'outgoing' && <span className="mx-1">{' → '}</span>}
              {direction === 'outgoing' && (
                <button onClick={() => onSelectNode(otherId)} className="text-emerald-400 hover:underline truncate">
                  {other?.Name || formatGuid(otherId)}
                </button>
              )}
              {onSelectEdge && (
                <button
                  onClick={() => onSelectEdge(r.Id)}
                  className="ml-auto pl-1 text-zinc-500 hover:text-blue-400 shrink-0"
                  title={t('panels.rel.openEdgeDetail')}
                >
                  <ExternalLink size={10} />
                </button>
              )}
            </div>
            {isExpanded && relProps.length > 0 && (
              <div className="ml-5 mb-1 pl-2 border-l-2 border-zinc-700">
                <EditablePropertyList
                  properties={relProps}
                  entityId={r.Id}
                  entityType="relationship"
                  editMode={editMode}
                  onSaved={onPropertySaved}
                />
              </div>
            )}
          </div>
        );
      })}
      {editMode && fixedThingId && (
        <AddRelationshipRow
          direction={direction}
          fixedThingId={fixedThingId}
          things={Array.from(allThings.values())}
          relationships={allRelationships ?? relationships}
          onCreated={onRelationshipCreated}
        />
      )}
    </div>
  );
}
