import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { Edge, Node } from '@xyflow/react';
import { PipelineModel, ROLE } from '../pipeline/model';
import type { OutputRow } from '../pipeline/catalysts';
import type { PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import { useWiderList } from './useWiderList';

interface PipelineSelectionInputs {
  model: PipelineModel;
  nodes: Node[];
  edges: Edge[];
  outputs: OutputRow[];
  currentNodes: () => Node[];
}

export function usePipelineSelection({ model, nodes, edges, outputs, currentNodes }: PipelineSelectionInputs) {
  const { t } = useTranslation();
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null);

  const selectedNode = selectedNodeId ? nodes.find((n) => n.id === selectedNodeId) : undefined;
  const selectedEdge = selectedEdgeId ? edges.find((e) => e.id === selectedEdgeId) : undefined;

  const selectNode = useCallback((nodeId: string) => { setSelectedNodeId(nodeId); setSelectedEdgeId(null); }, []);
  const selectEdge = useCallback((edgeId: string) => { setSelectedEdgeId(edgeId); setSelectedNodeId(null); }, []);
  const clearSelection = useCallback(() => { setSelectedNodeId(null); setSelectedEdgeId(null); }, []);
  const closeNodeInspector = useCallback(() => setSelectedNodeId(null), []);
  const closeWireInspector = useCallback(() => setSelectedEdgeId(null), []);

  const resultsOf = useCallback((nodeId: string) => {
    const success = model.boundTo(ROLE.Success);
    const fail = model.boundTo(ROLE.Fail);
    const choices = (nodes.find((n) => n.id === nodeId)?.data as PipelineNodeData | undefined)?.choices ?? [];
    return [
      ...(success ? [{ id: success, name: t('pipeline.resultSuccess') }] : []),
      ...(fail ? [{ id: fail, name: t('pipeline.resultFail') }] : []),
      ...choices.map((choice) => ({ id: choice.id, name: choice.name })),
    ];
  }, [model, nodes, t]);

  const wiredInputs = useMemo(
    () => new Set(selectedNode ? edges.filter((e) => e.target === selectedNode.id).map((e) => e.targetHandle ?? '') : []),
    [selectedNode, edges],
  );

  const offeredByPort = useMemo(() => Object.fromEntries(
    ((selectedNode?.data as PipelineNodeData | undefined)?.ports ?? [])
      .filter((p) => p.direction === 'in')
      .map((p) => [p.portId, model.reachedAlong(p.portId, ROLE.Choice)] as const)),
  [selectedNode, model]);
  const [widened, setWidened] = useState(false);
  const selectedPorts = (selectedNode?.data as PipelineNodeData | undefined)?.ports;
  const kindsOffered = useMemo(
    () => (widened && selectedPorts ? model.kindsOffered(selectedPorts) : null),
    [widened, selectedPorts, model],
  );
  const referencedByTheDrawing = useCallback(() => new Set(currentNodes().flatMap((n) => {
    const d = n.data as unknown as PipelineNodeData;
    return [...Object.values(d.chosen ?? {}), ...(d.choices ?? []).flatMap((choice) => choice.comparesWith)];
  })), [currentNodes]);
  useWiderList(kindsOffered, referencedByTheDrawing);
  const widerList = useMemo(() => (kindsOffered ? model.thingsOfKinds(kindsOffered) : []), [model, kindsOffered]);
  const nameOf = useCallback((thingId: string) => model.thing(thingId)?.Name ?? thingId, [model]);
  const selectedKind = (selectedNode?.data as PipelineNodeData | undefined)?.kind;
  const standsForOptions = useMemo(() => outputs
    .filter((row) => (selectedKind === 'tell' && row.kind === 'externalSystem') || (selectedKind === 'followOn' && row.kind === 'pipeline'))
    .map((row) => ({ id: row.id, name: row.name })), [outputs, selectedKind]);

  const portNameOf = (nodeId: string, portId: string | null | undefined) =>
    (nodes.find((n) => n.id === nodeId)?.data as PipelineNodeData | undefined)?.ports.find((p) => p.portId === portId)?.portName ?? '';

  return {
    selectedNode, selectedEdge, selectNode, selectEdge, clearSelection, closeNodeInspector, closeWireInspector,
    resultsOf, wiredInputs, offeredByPort, widerList, setWidened, nameOf, standsForOptions, portNameOf,
  };
}
