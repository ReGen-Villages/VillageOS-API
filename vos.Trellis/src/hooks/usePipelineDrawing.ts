import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { addEdge, useEdgesState, useNodesState, type Connection, type Edge, type Node } from '@xyflow/react';
import { PipelineModel, ROLE, judgeConnection, type ConnectionInformation, type PortInformation } from '../pipeline/model';
import { savePipeline, loadPipeline, takenOnOf, type EditorNode, type EditorEdge, type EditorChoice } from '../pipeline/serialize';
import { wireDrawing, withoutChoices, type WireData } from '../pipeline/wireDrawing';
import { validatePipeline, validateEnds } from '../pipeline/validate';
import { type CatalystRow, type OutputRow } from '../pipeline/catalysts';
import { passedOn } from '../pipeline/passedOn';
import { handStateToOrchestrator } from '../pipeline/handOver';
import { relationshipApi } from '../api/relationshipApi';
import type { PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import type { WirePaths } from '../components/pipeline/WireInspector';
import { useEditorHistory } from './useEditorHistory';

let nodeSequence = 0;

interface PipelineDrawingInputs {
  model: PipelineModel;
  connections: ConnectionInformation[];
  setError: (error: string | null) => void;
}

export function usePipelineDrawing({ model, connections, setError }: PipelineDrawingInputs) {
  const { t } = useTranslation();
  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [name, setName] = useState(() => t('pipeline.newPipelineName'));
  const [savedId, setSavedId] = useState<string | null>(null);
  // The persistent id of the pipeline being edited — kept across edits (which clear savedId to mark the
  // canvas dirty) so a save UPDATES the loaded pipeline in place instead of forking a duplicate.
  const [editingPipelineId, setEditingPipelineId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [thingIdByCanvasId, setThingIdByCanvasId] = useState<Record<string, string>>({});

  const markUnsaved = useCallback(() => setSavedId(null), []);
  const history = useEditorHistory({ nodes, edges, setNodes, setEdges, onUndone: markUnsaved });
  const { recordSnapshot, commitBaseline, rollBack, current } = history;

  // Checked before a run and shown under the canvas: an input nothing fills, a wire to nowhere, or an end
  // standing for something a run cannot come from or leave behind. Run stays off until the list is empty,
  // so a broken pipeline is refused where it is drawn rather than failing once it runs.
  const validationIssues = useMemo(() => {
    const validationNodes = nodes.map((n) => {
      const d = n.data as unknown as PipelineNodeData;
      const stood = d.standsForId ? model.endInformation(d.standsForId) : undefined;
      return {
        id: n.id, label: d.label, ports: d.ports, parameterBindings: d.parameterBindings, kind: d.kind,
        ...(stood ? { standsFor: { name: stood.name, kind: stood.kind, mayStart: stood.mayStart, mayEnd: stood.mayEnd } } : {}),
      };
    });
    const validationEdges = edges.map((e) => ({
      source: e.source,
      sourceHandle: e.sourceHandle ?? '',
      target: e.target,
      targetHandle: e.targetHandle ?? '',
    }));
    return [...validatePipeline(validationNodes, validationEdges), ...validateEnds(validationNodes, validationEdges)];
  }, [nodes, edges, model]);

  // The drawing as the canvas paints it: a node says what it stands for, read off the model so a rename reaches
  // it, and a start node says that placing it is not what starts a run.
  const canvasNodes = useMemo(() => nodes.map((n) => {
    const d = n.data as unknown as PipelineNodeData;
    if (!d.kind || !d.standsForId) return n;
    const standsFor = model.endInformation(d.standsForId);
    const note = d.kind === 'input' ? t('pipeline.note.startNotCalled') : undefined;
    return { ...n, data: { ...n.data, standsFor, note } };
  }), [nodes, model, t]);

  const canvasEdges = useMemo(() => {
    const fail = model.boundTo(ROLE.Fail);
    const choiceNames = new Map(nodes.flatMap((n) =>
      ((n.data as unknown as PipelineNodeData).choices ?? []).map((choice) => [choice.id, choice.name] as const)));
    return edges.map((e) => {
      const data = (e.data as WireData | undefined) ?? {};
      return { ...e, ...wireDrawing(data, takenOnOf(data.takenOn, model), fail, choiceNames) };
    });
  }, [edges, nodes, model]);

  const setEdgePath = useCallback((edgeId: string, which: keyof WirePaths, value: string) => {
    recordSnapshot();
    setEdges((es) => es.map((e) => (e.id === edgeId ? { ...e, data: { ...e.data, [which]: value || undefined } } : e)));
    setSavedId(null);
  }, [setEdges, recordSnapshot]);

  const setEdgeTakenOn = useCallback((edgeId: string, takenOn: string[]) => {
    recordSnapshot();
    setEdges((es) => es.map((e) => (e.id === edgeId ? { ...e, data: { ...e.data, takenOn } } : e)));
    setSavedId(null);
  }, [setEdges, recordSnapshot]);

  const place = useCallback((data: PipelineNodeData) => {
    recordSnapshot();
    setNodes((ns) => [
      ...ns,
      {
        id: `n${++nodeSequence}`,
        type: 'pipelineNode',
        position: { x: 80 + ns.length * 60, y: 80 + ns.length * 40 },
        data: data as unknown as Record<string, unknown>,
      },
    ]);
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  const addNode = useCallback((c: ConnectionInformation) => {
    place({ label: c.name, connectionId: c.connectionId, subdomain: c.subdomain, ports: c.ports });
  }, [place]);

  // Where the run comes from: a start node standing for the catalyst it was placed from, whose out port
  // is the run's parameter — the message that arrived, or the Thing that entered the state or the
  // relationship. Placed from nothing, it is a start by hand, filled from the parameters typed above.
  const placeStart = useCallback((row: CatalystRow | undefined) => {
    const portName = row === undefined || row.kind === 'message' ? 'payload' : 'subject';
    place({
      label: row ? (row.who ? `${row.who} · ${row.what}` : row.what) : t('pipeline.boundaryInput'),
      kind: 'input',
      ports: [{ portId: crypto.randomUUID(), portName, direction: 'out', type: 'any', required: false }],
      ...(row ? { standsForId: row.standsForId } : {}),
    });
  }, [place, t]);

  // The rail's words after a hand-over come from the binding the stream delivers, not from the press.
  const handOver = useCallback(async (row: CatalystRow) => {
    setBusy(true);
    setError(null);
    try {
      await handStateToOrchestrator(model, row, relationshipApi);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.catalysts.handOverFailed'));
    } finally {
      setBusy(false);
    }
  }, [model, setError, t]);

  const placeOutput = useCallback((row: OutputRow) => {
    if (row.kind === 'answer')
      place({
        label: t('pipeline.boundaryOutput'),
        kind: 'output',
        ports: [{ portId: crypto.randomUUID(), portName: 'value', direction: 'in', type: 'any', required: true }],
      });
    else if (row.kind === 'stop')
      place({ label: t('pipeline.outputs.stop'), kind: 'stop', ports: model.resolvePorts(model.boundTo(ROLE.PipelineStop)!) });
    else
      place({
        label: row.name,
        kind: row.kind === 'pipeline' ? 'followOn' : 'tell',
        ports: passedOn(row.kind === 'pipeline' ? 'subject' : 'payload'),
        standsForId: row.id,
      });
  }, [place, model, t]);

  const setDeclaredPorts = useCallback((nodeId: string, ports: PortInformation[]) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, ports } } : n)));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  const setStandsFor = useCallback((nodeId: string, standsForId: string) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, standsForId } } : n)));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  const onConnect = useCallback((c: Connection) => {
    const verdict = judgeConnection(nodes.map((n) => ({ id: n.id, ports: (n.data as PipelineNodeData).ports })), c);
    if (verdict.kind === 'ignore') return;
    if (verdict.kind === 'incompatible') {
      setError(t('pipeline.incompatibleWire', { from: verdict.from, to: verdict.to }));
      return;
    }
    setError(null);
    recordSnapshot();
    setEdges((es) => addEdge(c, es));
    setSavedId(null);
  }, [nodes, setEdges, recordSnapshot, setError, t]);

  const toEditorNodes = (): EditorNode[] =>
    nodes.map((n) => {
      const d = n.data as unknown as PipelineNodeData;
      return { id: n.id, connectionId: d.connectionId ?? '', label: d.label, x: n.position.x, y: n.position.y, ports: d.ports, parameterBindings: d.parameterBindings, kind: d.kind, standsForId: d.standsForId, choices: d.choices, chosen: d.chosen, rule: d.rule };
    });

  const toEditorEdges = (): EditorEdge[] =>
    edges.map((e) => ({
      id: e.id,
      source: e.source,
      sourceHandle: e.sourceHandle ?? '',
      target: e.target,
      targetHandle: e.targetHandle ?? '',
      fromPath: (e.data as WireData | undefined)?.fromPath,
      itemTransform: (e.data as WireData | undefined)?.itemTransform,
      listTransform: (e.data as WireData | undefined)?.listTransform,
      toPath: (e.data as WireData | undefined)?.toPath,
      takenOn: (e.data as WireData | undefined)?.takenOn,
    }));

  /** Answers whether the save went through. */
  const save = useCallback(async () => {
    // Optimistic rollback: remember the state we are trying to save so a rejected save can revert
    // the canvas to the last server-confirmed state instead of leaving it out of step with the server.
    const attempt = structuredClone(current());
    setBusy(true);
    setError(null);
    try {
      const saved = await savePipeline(name, toEditorNodes(), toEditorEdges(), model, editingPipelineId ?? undefined);
      setEditingPipelineId(saved.pipelineId);
      setSavedId(saved.pipelineId);
      setThingIdByCanvasId(saved.nodeIdMap);
      setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
      commitBaseline(attempt); // the saved canvas is the new baseline; undo history clears
      return true;
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.saveFailed'));
      // Only a pipeline already saved has a server-confirmed state to go back to; a never-saved canvas keeps the user's work.
      if (editingPipelineId && rollBack()) setSavedId(editingPipelineId);
      return false;
    } finally {
      setBusy(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [name, nodes, edges, model, editingPipelineId, commitBaseline, rollBack, current, setError, t]);

  /** Answers whether the model holds the pipeline, so the canvas now shows it. */
  const load = useCallback((pipelineId: string) => {
    const loadedPipeline = loadPipeline(pipelineId, model);
    if (!loadedPipeline) return false;
    setName(loadedPipeline.name);
    setSavedId(pipelineId);
    setEditingPipelineId(pipelineId);
    setThingIdByCanvasId(Object.fromEntries(loadedPipeline.nodes.map((n) => [n.id, n.id])));
    const loadedNodes: Node[] = loadedPipeline.nodes.map((n) => ({
      id: n.id,
      type: 'pipelineNode',
      position: { x: n.x, y: n.y },
      data: { label: n.label, kind: n.kind, connectionId: n.connectionId, subdomain: connections.find((c) => c.connectionId === n.connectionId)?.subdomain ?? '', ports: n.ports, parameterBindings: n.parameterBindings, standsForId: n.standsForId, choices: n.choices, chosen: n.chosen, rule: n.rule } as unknown as Record<string, unknown>,
    }));
    const loadedEdges: Edge[] = loadedPipeline.edges.map((e) => ({ id: e.id, source: e.source, sourceHandle: e.sourceHandle, target: e.target, targetHandle: e.targetHandle, data: { fromPath: e.fromPath, toPath: e.toPath, itemTransform: e.itemTransform, listTransform: e.listTransform, takenOn: e.takenOn } }));
    setNodes(loadedNodes);
    setEdges(loadedEdges);
    commitBaseline({ nodes: loadedNodes, edges: loadedEdges }); // a freshly loaded pipeline is the undo/rollback floor
    return true;
  }, [model, connections, setNodes, setEdges, commitBaseline]);

  const startNew = useCallback((newName?: string) => {
    setNodes([]);
    setEdges([]);
    setName(newName ?? t('pipeline.newPipelineName'));
    setSavedId(null);
    setEditingPipelineId(null);
    setThingIdByCanvasId({});
    setError(null);
    commitBaseline({ nodes: [], edges: [] }); // empty canvas is the floor; nothing to undo/roll back to
  }, [setNodes, setEdges, commitBaseline, setError, t]);

  const setBinding = useCallback((nodeId: string, port: string, parameterKey: string) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => {
      if (n.id !== nodeId) return n;
      const d = n.data as unknown as PipelineNodeData;
      const next = { ...(d.parameterBindings ?? {}) };
      if (parameterKey.trim()) next[port] = parameterKey.trim();
      else delete next[port];
      return { ...n, data: { ...n.data, parameterBindings: next } };
    }));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  const setChosen = useCallback((nodeId: string, chosen: Record<string, string>) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, chosen } } : n)));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  const setChoices = useCallback((nodeId: string, choices: EditorChoice[]) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, choices } } : n)));
    const kept = new Set(choices.map((choice) => choice.id));
    const removed = new Set(((nodes.find((n) => n.id === nodeId)?.data as PipelineNodeData | undefined)?.choices ?? [])
      .map((choice) => choice.id).filter((id) => !kept.has(id)));
    if (removed.size > 0) setEdges((es) => withoutChoices(es, nodeId, removed));
    setSavedId(null);
  }, [nodes, setNodes, setEdges, recordSnapshot]);

  const setRule = useCallback((nodeId: string, rule: string | undefined) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, rule } } : n)));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  // Record once at the start of a node drag (not per position tick), and before a delete — so undo restores
  // the pre-move / pre-delete canvas. Deletes also mark the canvas dirty relative to the last save.
  const onNodeDragStart = useCallback(() => recordSnapshot(), [recordSnapshot]);
  const onNodesDelete = useCallback(() => { recordSnapshot(); setSavedId(null); }, [recordSnapshot]);
  const onEdgesDelete = useCallback(() => { recordSnapshot(); setSavedId(null); }, [recordSnapshot]);

  const currentNodes = useCallback(() => current().nodes, [current]);

  return {
    nodes, edges, setNodes, onNodesChange, onEdgesChange, canvasNodes, canvasEdges, validationIssues,
    name, setName, savedId, editingPipelineId, busy, thingIdByCanvasId, currentNodes,
    canUndo: history.canUndo, dirty: history.dirty, onUndo: history.onUndo,
    save, load, startNew, place, addNode, placeStart, placeOutput, handOver,
    setEdgePath, setEdgeTakenOn, setDeclaredPorts, setStandsFor, onConnect, setBinding, setChosen, setChoices, setRule,
    onNodeDragStart, onNodesDelete, onEdgesDelete,
  };
}
