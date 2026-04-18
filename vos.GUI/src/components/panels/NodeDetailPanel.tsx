import { useState, useEffect, useMemo, lazy, Suspense } from 'react';
import { X, Copy, Pencil, ChevronDown, ChevronRight, RefreshCw } from 'lucide-react';
import { EditablePropertyList } from './EditablePropertyList';
import { RelationshipList } from './RelationshipList';
import { RangesTabContent } from './RangesTabContent';
import type { VosThing, VosRelationship, InheritedPropertySet, EffectiveProperty } from '../../types/vos';
import { formatGuid } from '../../utils/formatters';
import { thingApi } from '../../api/thingApi';
import { useNodeRangesData } from '../../hooks/useNodeRangesData';
import { useUiStore } from '../../stores/uiStore';
import { toast } from '../common/Toast';
import { canSupport3D } from '../../utils/browserDetect';

const BuildingDetail3D = lazy(() => import('../three/BuildingDetail3D'));
import type { ChildElement } from '../three/BuildingDetail3D';

interface Props {
  thing: VosThing;
  relationships: VosRelationship[];
  allThings: Map<string, VosThing>;
  onClose: () => void;
  onSelectNode: (id: string) => void;
  onDeleteProperty: (thingId: string, propertyName: string) => void;
  onPropertySet?: () => void;
  statesVersion?: number;
}

export function NodeDetailPanel({ thing, relationships, allThings, onClose, onSelectNode, onDeleteProperty, onPropertySet, statesVersion }: Props) {
  const [tab, setTab] = useState<'properties' | 'relationships' | 'ranges' | '3d'>('ranges');
  const [effectiveProps, setEffectiveProps] = useState<Record<string, EffectiveProperty> | null>(null);
  const [expandedValue, setExpandedValue] = useState<{ name: string; value: string } | null>(null);
  const [editMode, setEditMode] = useState(false);
  const [relEditMode, setRelEditMode] = useState(false);

  const selectEdge = useUiStore((s) => s.selectEdge);
  const hasGeometry = thing.Properties?.geometry != null;

  // For IFC things, gather child elements with geometry via contains/aggregates
  const childElements = useMemo<ChildElement[]>(() => {
    const isIfcThing = typeof thing.Properties?.ifcClass === 'string';
    if (!isIfcThing) return [];

    const spatialPredicateIds = new Set<string>();
    for (const [, t] of allThings) {
      if (t.Properties?.__IsMapContainmentPredicate === true) spatialPredicateIds.add(t.Id);
    }
    if (spatialPredicateIds.size === 0) return [];

    const children: ChildElement[] = [];
    for (const rel of relationships) {
      if (rel.SubjectId !== thing.Id) continue;
      if (!spatialPredicateIds.has(rel.PredicateId)) continue;

      const child = allThings.get(rel.TargetId);
      if (!child?.Properties?.geometry) continue;

      children.push({
        geometryValue: child.Properties.geometry,
        name: child.Name,
        ifcClass: typeof child.Properties.ifcClass === 'string'
          ? child.Properties.ifcClass
          : undefined,
      });
    }
    return children;
  }, [thing, relationships, allThings]);

  const hasChildGeometry = childElements.length > 0;
  const show3DTab = (hasGeometry || hasChildGeometry) && canSupport3D();

  const outgoing = relationships.filter((r) => r.SubjectId === thing.Id);
  const incoming = relationships.filter((r) => r.TargetId === thing.Id);

  const { rangesData, statesData, rangesLoading, relRangesEntries, refresh: refreshRanges } = useNodeRangesData(
    thing.Id, tab, statesVersion,
  );

  const props = thing.Properties ? Object.entries(thing.Properties) : [];
  const [propsVersion, setPropsVersion] = useState(0);

  // Fetch effective properties (own + inherited with source info)
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const ep = await thingApi.getEffectiveProperties(thing.Id);
        if (!cancelled) setEffectiveProps(ep);
      } catch {
        if (!cancelled) setEffectiveProps(null);
      }
    })();
    return () => { cancelled = true; };
  }, [thing.Id, propsVersion]);

  const handlePropertySaved = () => {
    setPropsVersion((v) => v + 1);
    onPropertySet?.();
  };

  const handleExpandValue = (name: string, value: string) => setExpandedValue({ name, value });

  const copyId = () => {
    navigator.clipboard.writeText(thing.Id);
    toast.info('ID copied');
  };

  return (
    <div className="flex-1 flex flex-col overflow-hidden">
      <div className="p-3 border-b border-zinc-200 dark:border-zinc-700 flex items-center justify-between">
        <div className="min-w-0">
          <h3 className="font-semibold text-sm truncate">{thing.Name}</h3>
          <button onClick={copyId} className="flex items-center gap-1 text-xs text-zinc-500 hover:text-zinc-300">
            <Copy size={10} /> {formatGuid(thing.Id)}
          </button>
        </div>
        <button onClick={onClose} className="text-zinc-400 hover:text-zinc-200">
          <X size={16} />
        </button>
      </div>

      <div className="flex items-center border-b border-zinc-200 dark:border-zinc-700">
        {(['ranges', 'properties', 'relationships'] as const).map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`flex-1 px-2 py-1.5 text-xs font-medium capitalize ${
              tab === t ? 'text-blue-500 border-b-2 border-blue-500' : 'text-zinc-500 hover:text-zinc-300'
            }`}
          >
            {t}
          </button>
        ))}
        {show3DTab && (
          <button
            onClick={() => setTab('3d')}
            className={`flex-1 px-2 py-1.5 text-xs font-medium ${
              tab === '3d' ? 'text-blue-500 border-b-2 border-blue-500' : 'text-zinc-500 hover:text-zinc-300'
            }`}
          >
            3D
          </button>
        )}
        {tab === 'ranges' && (
          <button
            onClick={refreshRanges}
            className="px-1.5 py-1 text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700 rounded transition-colors"
            title="Refresh ranges"
          >
            <RefreshCw size={12} />
          </button>
        )}
      </div>

      <div className="flex-1 overflow-auto p-3 text-sm">
        {tab === 'properties' && (
          <div className="space-y-3">
            <div className="space-y-1">
              <div className="flex items-center justify-between mb-1">
                <h4 className="text-xs font-semibold text-zinc-500">Own ({props.length})</h4>
                <button
                  onClick={() => setEditMode((v) => !v)}
                  className={`p-0.5 rounded transition-colors ${
                    editMode
                      ? 'text-blue-400 bg-blue-500/20 hover:bg-blue-500/30'
                      : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
                  }`}
                  title={editMode ? 'Exit edit mode' : 'Edit properties'}
                >
                  <Pencil size={12} />
                </button>
              </div>
              <EditablePropertyList
                properties={props}
                entityId={thing.Id}
                entityType="thing"
                editMode={editMode}
                onSaved={handlePropertySaved}
                onDeleteProperty={(name) => onDeleteProperty(thing.Id, name)}
                onExpandValue={handleExpandValue}
              />
            </div>

            {effectiveProps && <InheritedPropertiesSection
              effectiveProps={effectiveProps}
              allThings={allThings}
              onSelectNode={onSelectNode}
              onExpandValue={handleExpandValue}
              editMode={editMode}
              entityId={thing.Id}
              onSaved={handlePropertySaved}
            />}

            {thing.InheritedProperties && Object.keys(thing.InheritedProperties).length > 0 && (
              <div className="space-y-2">
                <h4 className="text-xs font-semibold text-zinc-500 mb-1">Inheritance Chain</h4>
                {Object.entries(thing.InheritedProperties).map(([sourceId, ips]) => (
                  <InheritedPropertySetView
                    key={sourceId}
                    ips={ips}
                    onSelectNode={onSelectNode}
                    onExpandValue={handleExpandValue}
                    editMode={editMode}
                    entityId={thing.Id}
                    onSaved={handlePropertySaved}
                  />
                ))}
              </div>
            )}
          </div>
        )}

        {tab === 'relationships' && (
          <div className="space-y-3">
            <div className="flex items-center justify-end">
              <button
                onClick={() => setRelEditMode((v) => !v)}
                className={`p-0.5 rounded transition-colors ${
                  relEditMode
                    ? 'text-blue-400 bg-blue-500/20 hover:bg-blue-500/30'
                    : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
                }`}
                title={relEditMode ? 'Exit edit mode' : 'Edit relationship properties'}
              >
                <Pencil size={12} />
              </button>
            </div>
            <LogicalChildrenSection
              thingId={thing.Id}
              relationships={relationships}
              allThings={allThings}
              hasGeometry={hasGeometry}
              onSelectNode={onSelectNode}
            />
            <RelationshipList relationships={outgoing} direction="outgoing" allThings={allThings} onSelectNode={onSelectNode} onSelectEdge={selectEdge} editMode={relEditMode} onPropertySaved={onPropertySet} fixedThingId={thing.Id} allRelationships={relationships} onRelationshipCreated={onPropertySet} />
            <RelationshipList relationships={incoming} direction="incoming" allThings={allThings} onSelectNode={onSelectNode} onSelectEdge={selectEdge} editMode={relEditMode} onPropertySaved={onPropertySet} fixedThingId={thing.Id} allRelationships={relationships} onRelationshipCreated={onPropertySet} />
          </div>
        )}

        {tab === 'ranges' && (
          <div className="space-y-3">
            <RangesTabContent
              rangesData={rangesData}
              statesData={statesData}
              loading={rangesLoading}
              onSelectNode={onSelectNode}
              relationshipRanges={relRangesEntries}
              entityId={thing.Id}
              editable
              onRangeChanged={refreshRanges}
            />
          </div>
        )}

        {tab === '3d' && show3DTab && (
          <Suspense
            fallback={
              <div className="h-64 flex items-center justify-center text-zinc-500 text-sm">
                Loading 3D...
              </div>
            }
          >
            <BuildingDetail3D
              key={thing.Id}
              geometryValue={thing.Properties?.geometry}
              color={undefined}
              childElements={hasChildGeometry ? childElements : undefined}
            />
          </Suspense>
        )}
      </div>

      {expandedValue && (
        <div className="absolute inset-0 z-20 flex flex-col bg-zinc-900/95 backdrop-blur">
          <div className="flex items-center justify-between p-3 border-b border-zinc-700">
            <h3 className="text-xs font-semibold text-zinc-300 truncate">{expandedValue.name}</h3>
            <div className="flex items-center gap-1">
              <button
                onClick={() => {
                  navigator.clipboard.writeText(expandedValue.value);
                  toast.info('Value copied');
                }}
                className="text-zinc-400 hover:text-zinc-200 p-1"
                title="Copy value"
              >
                <Copy size={14} />
              </button>
              <button
                onClick={() => setExpandedValue(null)}
                className="text-zinc-400 hover:text-zinc-200 p-1"
                title="Close"
              >
                <X size={14} />
              </button>
            </div>
          </div>
          <textarea
            readOnly
            value={expandedValue.value}
            className="flex-1 p-3 text-xs font-mono bg-transparent text-zinc-300 resize-none focus:outline-none"
            spellCheck={false}
          />
        </div>
      )}
    </div>
  );
}

// ── Shared collapsible header for inherited property groups ──────────────

function CollapsiblePropertyGroup({ label, count, onNavigate, expanded, onToggle, editMode, entityId, properties, onSaved, onExpandValue, className }: {
  label: string;
  count: number;
  onNavigate: () => void;
  expanded: boolean;
  onToggle: () => void;
  editMode: boolean;
  entityId: string;
  properties: [string, unknown][];
  onSaved?: () => void;
  onExpandValue: (name: string, value: string) => void;
  className?: string;
}) {
  return (
    <div className={className}>
      <div className="flex items-center gap-1">
        <button onClick={onToggle} className="text-zinc-500 hover:text-zinc-300 shrink-0">
          {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
        </button>
        <button
          onClick={onNavigate}
          className="text-[11px] text-amber-400/80 hover:text-amber-400 hover:underline truncate"
          title={`Navigate to ${label}`}
        >
          {label}
        </button>
        <span className="text-[10px] text-zinc-600 shrink-0">({count})</span>
      </div>
      {expanded && (
        <div className="ml-4">
          <EditablePropertyList
            properties={properties}
            entityId={entityId}
            entityType="thing"
            editMode={editMode}
            onSaved={onSaved}
            onExpandValue={onExpandValue}
            showAddRow={false}
          />
        </div>
      )}
    </div>
  );
}

// ── Inherited properties from effective-properties API ──────────────────

function InheritedPropertiesSection({ effectiveProps, allThings, onSelectNode, onExpandValue, editMode, entityId, onSaved }: {
  effectiveProps: Record<string, EffectiveProperty>;
  allThings: Map<string, VosThing>;
  onSelectNode: (id: string) => void;
  onExpandValue: (name: string, value: string) => void;
  editMode: boolean;
  entityId: string;
  onSaved?: () => void;
}) {
  const [expandedSources, setExpandedSources] = useState<Set<string>>(new Set());
  const inherited = Object.entries(effectiveProps).filter(([, ep]) => ep.IsInherited);
  if (inherited.length === 0) return null;

  // Group by source thing
  const bySource = new Map<string, { name: string; props: [string, unknown][] }>();
  for (const [name, ep] of inherited) {
    const sourceId = ep.InheritedFrom || 'unknown';
    if (!bySource.has(sourceId)) {
      bySource.set(sourceId, { name: allThings.get(sourceId)?.Name || formatGuid(sourceId), props: [] });
    }
    bySource.get(sourceId)!.props.push([name, ep.Value]);
  }

  const toggleSource = (id: string) => setExpandedSources((prev) => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  return (
    <div className="space-y-2">
      {[...bySource.entries()].map(([sourceId, { name: sourceName, props }]) => (
        <CollapsiblePropertyGroup
          key={sourceId}
          label={sourceName}
          count={props.length}
          onNavigate={() => onSelectNode(sourceId)}
          expanded={expandedSources.has(sourceId)}
          onToggle={() => toggleSource(sourceId)}
          editMode={editMode}
          entityId={entityId}
          properties={props}
          onSaved={onSaved}
          onExpandValue={onExpandValue}
          className="space-y-1"
        />
      ))}
    </div>
  );
}

// ── Logical children (non-geo nodes linked to a geo node) ───────────────

function LogicalChildrenSection({ thingId, relationships, allThings, hasGeometry, onSelectNode }: {
  thingId: string;
  relationships: VosRelationship[];
  allThings: Map<string, VosThing>;
  hasGeometry: boolean;
  onSelectNode: (id: string) => void;
}) {
  const expandedLogicalParents = useUiStore((s) => s.expandedLogicalParents);
  const toggleLogicalExpansion = useUiStore((s) => s.toggleLogicalExpansion);

  const logicalChildren = useMemo(() => {
    if (!hasGeometry) return [];

    const childIds = new Set<string>();
    for (const r of relationships) {
      if (r.SubjectId === thingId) {
        const target = allThings.get(r.TargetId);
        if (target && !(target.Properties && 'geometry' in target.Properties)) {
          childIds.add(r.TargetId);
        }
      }
      if (r.TargetId === thingId) {
        const subject = allThings.get(r.SubjectId);
        if (subject && !(subject.Properties && 'geometry' in subject.Properties)) {
          childIds.add(r.SubjectId);
        }
      }
    }
    return [...childIds].map((id) => allThings.get(id)).filter(Boolean) as VosThing[];
  }, [thingId, relationships, allThings, hasGeometry]);

  if (logicalChildren.length === 0) return null;

  return (
    <div>
      <div className="flex items-center justify-between mb-1">
        <h4 className="text-xs font-semibold text-zinc-500">
          Logical Nodes ({logicalChildren.length})
        </h4>
        <button
          onClick={() => toggleLogicalExpansion(thingId)}
          className={`flex items-center gap-0.5 text-[10px] px-1.5 py-0.5 rounded transition-colors ${
            expandedLogicalParents.has(thingId)
              ? 'bg-blue-600/20 text-blue-400 hover:bg-blue-600/30'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
          title={expandedLogicalParents.has(thingId) ? 'Hide in graph' : 'Show in graph'}
        >
          {expandedLogicalParents.has(thingId) ? <ChevronDown size={10} /> : <ChevronRight size={10} />}
          {expandedLogicalParents.has(thingId) ? 'Shown' : 'Hidden'}
        </button>
      </div>
      {logicalChildren.map((child) => (
        <button
          key={child.Id}
          onClick={() => onSelectNode(child.Id)}
          className="block w-full text-left py-1 text-xs hover:bg-zinc-200 dark:hover:bg-zinc-800 rounded px-1"
        >
          <span className="text-purple-400">{child.Name || formatGuid(child.Id)}</span>
        </button>
      ))}
    </div>
  );
}

// ── Inheritance chain from thing.InheritedProperties ─────────────────────

function InheritedPropertySetView({ ips, onSelectNode, onExpandValue, editMode, entityId, onSaved, depth = 0 }: {
  ips: InheritedPropertySet;
  onSelectNode: (id: string) => void;
  onExpandValue: (name: string, value: string) => void;
  editMode: boolean;
  entityId: string;
  onSaved?: () => void;
  depth?: number;
}) {
  const [collapsed, setCollapsed] = useState(false);
  const inheritedProps = ips.Properties ? Object.entries(ips.Properties) : [];

  return (
    <div className={`${depth > 0 ? 'ml-3' : ''} border-l-2 border-zinc-700/50 pl-2`}>
      <CollapsiblePropertyGroup
        label={ips.SourceName}
        count={inheritedProps.length}
        onNavigate={() => onSelectNode(ips.SourceId)}
        expanded={!collapsed}
        onToggle={() => setCollapsed((v) => !v)}
        editMode={editMode}
        entityId={entityId}
        properties={inheritedProps}
        onSaved={onSaved}
        onExpandValue={onExpandValue}
      />
      {!collapsed && ips.Inherited && Object.entries(ips.Inherited).map(([nestedId, nestedIps]) => (
        <InheritedPropertySetView key={nestedId} ips={nestedIps} onSelectNode={onSelectNode} onExpandValue={onExpandValue} editMode={editMode} entityId={entityId} onSaved={onSaved} depth={depth + 1} />
      ))}
    </div>
  );
}
