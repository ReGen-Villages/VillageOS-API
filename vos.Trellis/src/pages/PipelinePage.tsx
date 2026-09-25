import { parseParameterValue } from './parseParamValue';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  ReactFlow,
  Background,
  Controls,
  addEdge,
  useNodesState,
  useEdgesState,
  type Node,
  type Edge,
  type Connection,
  type NodeTypes,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import clsx from 'clsx';
import { Play, Save, FilePlus, MousePointerClick, Ban, History, SlidersHorizontal, AlertTriangle, Undo2 } from 'lucide-react';
import { useModelStore } from '../stores/modelStore';
import { useThemeStore } from '../stores/themeStore';
import { useSubscription } from '../hooks/useSse';
import { WHOLE_MODEL } from '../types/subscription';
import { PipelineModel, ARCHETYPE_FLAG, typesCompatible, type ConnectionInformation, type PortInformation } from '../pipeline/model';
import { savePipeline, loadPipeline, type EditorNode, type EditorEdge } from '../pipeline/serialize';
import { validatePipeline, validateEnds } from '../pipeline/validate';
import { catalystRail, outputRail, type CatalystRow, type OutputRow } from '../pipeline/catalysts';
import { EditorHistory } from '../pipeline/history';
import { pipelineApi } from '../api/pipelineApi';
import { PipelineNodeView, type PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import { CatalystRail } from '../components/pipeline/CatalystRail';
import { OutputRail } from '../components/pipeline/OutputRail';
import { PipelineRoster } from '../components/pipeline/PipelineRoster';
import { NodeInspector } from '../components/pipeline/NodeInspector';
import { WireInspector, type WirePaths } from '../components/pipeline/WireInspector';

const nodeTypes: NodeTypes = { pipelineNode: PipelineNodeView };

const RUN_STATUS_COLOR: Record<string, string> = {
  running: 'text-blue-600',
  succeeded: 'text-green-600',
  failed: 'text-red-500',
  cancelled: 'text-amber-600',
  partial: 'text-orange-500',
};

let nodeSequence = 0;

/** A short edge label for a mapped wire, e.g. `user.id → a` with a trailing `ƒ` when the wire
 * carries a JSONata transform; undefined when the wire is a plain whole-payload pass-through. */
function pathLabel(fromPath?: string, toPath?: string, transform?: string): string | undefined {
  if (!fromPath && !toPath && !transform) return undefined;
  const paths = fromPath || toPath ? `${fromPath || '·'} → ${toPath || '·'}` : '';
  return transform ? `${paths} ƒ`.trim() : paths;
}

/**
 * Drawing the pipelines the orchestrator runs, around what sets each one off and what it leaves behind.
 *
 * A pipeline is model data — nodes, the connections they dispatch, and the wires between their ports — so
 * this page is Thing and relationship work with a canvas over it. The rail on the left lists every
 * catalyst the model holds, the roster every pipeline, and the rail on the right every outcome and every
 * service a node may dispatch. A boundary node stands for the catalyst or the outcome it was placed from.
 */
export function PipelinePage() {
  useSubscription(WHOLE_MODEL);
  const { t } = useTranslation();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const loaded = useModelStore((s) => s.loaded);
  // The canvas paints its own controls and background and has to be told which lighting it is in.
  const theme = useThemeStore((s) => s.theme);
  const model = useMemo(() => new PipelineModel(things, relationships), [things, relationships]);
  const connections = useMemo(() => model.connections(), [model]);
  const pipelines = useMemo(
    () => model.thingsOfArchetypeCarrying(ARCHETYPE_FLAG.Pipeline)
      .map((pipeline) => ({
        id: pipeline.Id,
        name: pipeline.Name,
        nodeCount: model.outgoing(pipeline.Id, 'has').filter((held) => model.isOfArchetypeCarrying(held.Id, ARCHETYPE_FLAG.PipelineNode)).length,
      }))
      .sort((a, b) => a.name.localeCompare(b.name)),
    [model],
  );

  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [name, setName] = useState(() => t('pipeline.newPipelineName'));
  const [savedId, setSavedId] = useState<string | null>(null);
  // The persistent id of the pipeline being edited — kept across edits (which clear savedId to mark the
  // canvas dirty) so a save UPDATES the loaded pipeline in place instead of forking a duplicate.
  const [editingPipelineId, setEditingPipelineId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [runId, setRunId] = useState<string | null>(null);
  const [thingIdByCanvasId, setThingIdByCanvasId] = useState<Record<string, string>>({});
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null);
  const [runParameterValues, setRunParameterValues] = useState<Record<string, string>>({});

  // Everything that sets a run off and everything one may leave behind, read again on every model flush,
  // because each group is a walk of the model.
  const catalysts = useMemo(() => catalystRail(model), [model]);
  const outputs = useMemo(() => outputRail(model, editingPipelineId), [model, editingPipelineId]);

  // Undo + optimistic rollback. The history records the editor state *before* each mutation (undo),
  // and holds the last server-confirmed state as a baseline (rollback on a rejected save). `canUndo`/`dirty`
  // drive the toolbar. A ref mirrors the latest nodes/edges so any handler can snapshot the current state.
  type EditorSnapshot = { nodes: Node[]; edges: Edge[] };
  const historyReference = useRef(new EditorHistory<EditorSnapshot>({ nodes: [], edges: [] }));
  const stateReference = useRef<EditorSnapshot>({ nodes, edges });
  // Mirrored after each commit rather than during render, so a handler that snapshots for undo
  // reads what is on screen — never values from a render React went on to discard.
  useEffect(() => { stateReference.current = { nodes, edges }; }, [nodes, edges]);
  const [canUndo, setCanUndo] = useState(false);
  const [dirty, setDirty] = useState(false);

  const recordSnapshot = useCallback(() => {
    historyReference.current.record(structuredClone(stateReference.current));
    setCanUndo(true);
    setDirty(true);
  }, []);

  const applySnapshot = useCallback((s: EditorSnapshot) => {
    setNodes(structuredClone(s.nodes));
    setEdges(structuredClone(s.edges));
  }, [setNodes, setEdges]);

  // Baseline = the last server-confirmed state; also clears the undo stack (a fresh save/load/new is the floor).
  const commitBaseline = useCallback((s: EditorSnapshot) => {
    historyReference.current.commit(structuredClone(s));
    setCanUndo(false);
    setDirty(false);
  }, []);

  const onUndo = useCallback(() => {
    const previous = historyReference.current.undo();
    if (!previous) return;
    applySnapshot(previous);
    setCanUndo(historyReference.current.canUndo());
    setDirty(true);
    setSavedId(null); // an undo leaves the canvas out of step with the last save
  }, [applySnapshot]);

  const parameterKeys = useMemo(() => {
    const keys = new Set<string>();
    for (const n of nodes) {
      const b = (n.data as unknown as PipelineNodeData).paramBindings;
      if (b) for (const k of Object.values(b)) keys.add(k);
    }
    return [...keys].sort();
  }, [nodes]);

  // Checked before a run and shown under the canvas: an input nothing fills, a wire to nowhere, or an end
  // standing for something a run cannot come from or leave behind. Run stays off until the list is empty,
  // so a broken pipeline is refused where it is drawn rather than failing once it runs.
  const validationIssues = useMemo(() => {
    const validationNodes = nodes.map((n) => {
      const d = n.data as unknown as PipelineNodeData;
      const stood = d.standsForId ? model.endInformation(d.standsForId) : undefined;
      return {
        id: n.id, label: d.label, ports: d.ports, paramBindings: d.paramBindings, kind: d.kind,
        ...(stood ? { standsFor: { name: stood.name, mayStart: stood.mayStart, mayEnd: stood.mayEnd } } : {}),
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

  // The drawing as the canvas paints it: a boundary node says what it stands for, read off the model so a
  // rename reaches it, and what really happens at that end today — placing a node where a run comes from
  // is not what dispatches it, and nothing sends to a system a run ends at yet.
  const canvasNodes = useMemo(() => nodes.map((n) => {
    const d = n.data as unknown as PipelineNodeData;
    if (!d.kind || !d.standsForId) return n;
    const standsFor = model.endInformation(d.standsForId);
    const note = d.kind === 'input'
      ? t('pipeline.note.startNotDispatched')
      : standsFor.kind === 'externalSystem'
        ? t('pipeline.note.endNotSent', { name: standsFor.name })
        : standsFor.kind === 'pipeline'
          ? t('pipeline.note.endNotStarted', { name: standsFor.name })
          : undefined;
    return { ...n, data: { ...n.data, standsFor, note } };
  }), [nodes, model, t]);

  const selectedNode = selectedNodeId ? nodes.find((n) => n.id === selectedNodeId) : undefined;
  const selectedEdge = selectedEdgeId ? edges.find((e) => e.id === selectedEdgeId) : undefined;

  const setEdgePath = useCallback((edgeId: string, which: keyof WirePaths, value: string) => {
    recordSnapshot();
    setEdges((es) => es.map((e) => {
      if (e.id !== edgeId) return e;
      const data = { ...(e.data as WirePaths | undefined), [which]: value || undefined };
      return { ...e, data, label: pathLabel(data.fromPath, data.toPath, data.transform) };
    }));
    setSavedId(null);
  }, [setEdges, recordSnapshot]);

  const liveRunStatus = runId ? model.runStatus(runId) : undefined;
  const runActive = !!runId && (liveRunStatus === undefined || liveRunStatus === 'running');
  const runs = useMemo(() => (savedId ? model.runsOf(savedId) : []), [savedId, model]);

  const clearStatuses = useCallback(() => {
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
  }, [setNodes]);

  const onSelectRun = useCallback((id: string) => {
    clearStatuses();
    setRunId(id || null);
  }, [clearStatuses]);

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
      ports: [{ portName, direction: 'out', type: 'any', required: false }],
      ...(row ? { standsForId: row.standsForId } : {}),
    });
  }, [place, t]);

  // What the run leaves behind: the answer to whoever started it, another pipeline, or a system told.
  const placeOutput = useCallback((row: OutputRow) => {
    const portName = row.kind === 'answer' ? 'value' : row.kind === 'pipeline' ? 'subject' : 'payload';
    place({
      label: row.kind === 'answer' ? t('pipeline.boundaryOutput') : row.name,
      kind: 'output',
      ports: [{ portName, direction: 'in', type: 'any', required: true }],
      ...(row.kind === 'answer' ? {} : { standsForId: row.id }),
    });
  }, [place, t]);

  // Add / rename / remove a port on a boundary node (its ports are user-declared). Direction is fixed by the
  // node kind (Input → output ports, Output → input ports).
  const setBoundaryPorts = useCallback((nodeId: string, ports: PortInformation[]) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, ports } } : n)));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  const onConnect = useCallback((c: Connection) => {
    const source = nodes.find((n) => n.id === c.source)?.data as PipelineNodeData | undefined;
    const tgt = nodes.find((n) => n.id === c.target)?.data as PipelineNodeData | undefined;
    const outPort = source?.ports.find((p) => p.portName === c.sourceHandle && p.direction === 'out');
    const inPort = tgt?.ports.find((p) => p.portName === c.targetHandle && p.direction === 'in');
    if (!outPort || !inPort) return;
    if (!typesCompatible(outPort.type, inPort.type)) {
      setError(t('pipeline.incompatibleWire', { from: outPort.type || 'any', to: inPort.type || 'any' }));
      return;
    }
    setError(null);
    recordSnapshot();
    setEdges((es) => addEdge(c, es));
    setSavedId(null);
  }, [nodes, setEdges, recordSnapshot, t]);

  const toEditorNodes = (): EditorNode[] =>
    nodes.map((n) => {
      const d = n.data as unknown as PipelineNodeData;
      return { id: n.id, connectionId: d.connectionId ?? '', label: d.label, x: n.position.x, y: n.position.y, ports: d.ports, paramBindings: d.paramBindings, kind: d.kind, standsForId: d.standsForId };
    });

  const toEditorEdges = (): EditorEdge[] =>
    edges.map((e) => ({
      id: e.id,
      source: e.source,
      sourceHandle: e.sourceHandle ?? '',
      target: e.target,
      targetHandle: e.targetHandle ?? '',
      fromPath: (e.data as WirePaths | undefined)?.fromPath,
      transform: (e.data as WirePaths | undefined)?.transform,
      toPath: (e.data as WirePaths | undefined)?.toPath,
    }));

  const onSave = useCallback(async () => {
    // Optimistic rollback: remember the state we are trying to save so a rejected save can revert
    // the canvas to the last server-confirmed state instead of leaving it out of step with the server.
    const attempt = structuredClone(stateReference.current);
    setBusy(true);
    setError(null);
    try {
      const saved = await savePipeline(name, toEditorNodes(), toEditorEdges(), model, editingPipelineId ?? undefined);
      setEditingPipelineId(saved.pipelineId);
      setSavedId(saved.pipelineId);
      setThingIdByCanvasId(saved.nodeIdMap);
      setRunId(null);
      setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
      commitBaseline(attempt); // the saved canvas is the new baseline; undo history clears
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.saveFailed'));
      // Revert to the last server-confirmed state, if we have one (a never-saved canvas keeps the user's work).
      const target = historyReference.current.rollbackTarget();
      if (target) {
        applySnapshot(target);
        setCanUndo(false);
        setDirty(false);
        setSavedId(editingPipelineId);
      }
    } finally {
      setBusy(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [name, nodes, edges, model, editingPipelineId, commitBaseline, applySnapshot, t]);

  const onLoad = useCallback((pipelineId: string) => {
    const loadedPipeline = loadPipeline(pipelineId, model);
    if (!loadedPipeline) return;
    setName(loadedPipeline.name);
    setSavedId(pipelineId);
    setEditingPipelineId(pipelineId);
    setRunId(null);
    setSelectedNodeId(null);
    setSelectedEdgeId(null);
    setThingIdByCanvasId(Object.fromEntries(loadedPipeline.nodes.map((n) => [n.id, n.id])));
    const loadedNodes: Node[] = loadedPipeline.nodes.map((n) => ({
      id: n.id,
      type: 'pipelineNode',
      position: { x: n.x, y: n.y },
      data: { label: n.label, kind: n.kind, connectionId: n.connectionId, subdomain: connections.find((c) => c.connectionId === n.connectionId)?.subdomain ?? '', ports: n.ports, paramBindings: n.paramBindings, standsForId: n.standsForId } as unknown as Record<string, unknown>,
    }));
    const loadedEdges: Edge[] = loadedPipeline.edges.map((e) => ({ id: e.id, source: e.source, sourceHandle: e.sourceHandle, target: e.target, targetHandle: e.targetHandle, label: pathLabel(e.fromPath, e.toPath, e.transform), data: { fromPath: e.fromPath, toPath: e.toPath, transform: e.transform } }));
    setNodes(loadedNodes);
    setEdges(loadedEdges);
    commitBaseline({ nodes: loadedNodes, edges: loadedEdges }); // a freshly loaded pipeline is the undo/rollback floor
  }, [model, connections, setNodes, setEdges, commitBaseline]);

  const onNew = useCallback((newName?: string) => {
    setNodes([]);
    setEdges([]);
    setName(newName ?? t('pipeline.newPipelineName'));
    setSavedId(null);
    setEditingPipelineId(null);
    setRunId(null);
    setThingIdByCanvasId({});
    setError(null);
    commitBaseline({ nodes: [], edges: [] }); // empty canvas is the floor; nothing to undo/roll back to
  }, [setNodes, setEdges, commitBaseline, t]);

  // The first pipeline opens on arrival, once the model has loaded; a model holding none leaves the
  // empty canvas, where naming a new one still works.
  const openedOnArrival = useRef(false);
  useEffect(() => {
    if (openedOnArrival.current || !loaded || pipelines.length === 0) return;
    openedOnArrival.current = true;
    onLoad(pipelines[0].id);
  }, [loaded, pipelines, onLoad]);

  const onRun = useCallback(async () => {
    if (!savedId) return;
    setError(null);
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
    try {
      // Async spawn — get the run id up front and let the SSE animation effect below light up nodes.
      // Param values are parsed as JSON when valid (so a list `["a","b"]` drives fan-out, `42`→number),
      // otherwise passed through as a plain string.
      const parameters = Object.fromEntries(parameterKeys.map((k) => [k, parseParameterValue(runParameterValues[k] ?? '')]));
      const accepted = await pipelineApi.spawnAsync(savedId, parameters);
      setRunId(accepted.runId);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.runFailed'));
    }
  }, [savedId, setNodes, parameterKeys, runParameterValues, t]);

  const setBinding = useCallback((nodeId: string, port: string, parameterKey: string) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => {
      if (n.id !== nodeId) return n;
      const d = n.data as unknown as PipelineNodeData;
      const next = { ...(d.paramBindings ?? {}) };
      if (parameterKey.trim()) next[port] = parameterKey.trim();
      else delete next[port];
      return { ...n, data: { ...n.data, paramBindings: next } };
    }));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  const onCancel = useCallback(async () => {
    if (!runId) return;
    try {
      await pipelineApi.cancel(runId);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.cancelFailed'));
    }
  }, [runId, t]);

  // Record once at the start of a node drag (not per position tick), and before a delete — so undo restores
  // the pre-move / pre-delete canvas. Deletes also mark the canvas dirty relative to the last save.
  const onNodeDragStart = useCallback(() => recordSnapshot(), [recordSnapshot]);
  const onNodesDelete = useCallback(() => { recordSnapshot(); setSavedId(null); }, [recordSnapshot]);
  const onEdgesDelete = useCallback(() => { recordSnapshot(); setSavedId(null); }, [recordSnapshot]);

  // Ctrl/Cmd+Z undoes the last editor change. Ignored while typing in a field so it doesn't hijack text undo.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && !e.shiftKey && e.key.toLowerCase() === 'z') {
        const el = e.target as HTMLElement | null;
        const tag = el?.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || el?.isContentEditable) return;
        e.preventDefault();
        onUndo();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onUndo]);

  // Drive the live animation: as Phloem writes NodeRun statuses, the model store updates over SSE → this
  // recomputes and paints each node's status ring (PipelineNodeView). Canvas ids map to Thing ids via the
  // save/load map so each NodeRun (keyed by Thing id) finds its node.
  useEffect(() => {
    if (!runId) return;
    const statuses = model.nodeRunStatuses(runId);
    const progress = model.nodeRunProgress(runId);
    if (Object.keys(statuses).length === 0 && Object.keys(progress).length === 0) return;
    setNodes((ns) => ns.map((n) => {
      const thingId = thingIdByCanvasId[n.id] ?? n.id;
      const status = statuses[thingId];
      const prog = progress[thingId];
      if (status === undefined && prog === undefined) return n;
      const d = n.data as unknown as PipelineNodeData;
      return { ...n, data: { ...n.data, status: status ?? d.status, progress: prog } };
    }));
  }, [runId, model, thingIdByCanvasId, setNodes]);

  const wiredInputs = useMemo(
    () => new Set(selectedNode ? edges.filter((e) => e.target === selectedNode.id).map((e) => e.targetHandle ?? '') : []),
    [selectedNode, edges],
  );

  return (
    <div className="flex h-full flex-col md:flex-row">
      <CatalystRail groups={catalysts} onPlace={placeStart} onOpenPipeline={onLoad} />
      <PipelineRoster pipelines={pipelines} openedPipelineId={editingPipelineId} onOpen={onLoad} onCreate={onNew} />
      <div className="flex-1 min-w-0 min-h-0 flex flex-col">
        <div className="flex flex-wrap items-center gap-2 p-2 border-b border-zinc-200 dark:border-zinc-700">
          <button onClick={() => onNew()} className="flex items-center gap-1 px-3 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 hover:border-blue-400">
            <FilePlus size={14} /> {t('pipeline.new')}
          </button>
          <button
            onClick={onUndo}
            disabled={!canUndo}
            title={t('pipeline.undoTitle')}
            aria-label={t('pipeline.undo')}
            className="flex items-center gap-1 px-3 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 hover:border-blue-400 disabled:opacity-40"
          >
            <Undo2 size={14} /> {t('pipeline.undo')}
          </button>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            aria-label={t('pipeline.name')}
            className="px-2 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
          />
          <button onClick={onSave} disabled={busy || nodes.length === 0} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-blue-600 text-white disabled:opacity-50">
            <Save size={14} /> {t('pipeline.save')}
          </button>
          {runActive ? (
            <button onClick={onCancel} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-amber-600 text-white">
              <Ban size={14} /> {t('pipeline.cancel')}
            </button>
          ) : (
            <button
              onClick={onRun}
              disabled={busy || !savedId || validationIssues.length > 0}
              title={validationIssues.length > 0 ? validationIssues.map((i) => i.message).join('\n') : undefined}
              className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-green-600 text-white disabled:opacity-50"
            >
              <Play size={14} /> {t('pipeline.run')}
            </button>
          )}
          {savedId && runs.length > 0 && (
            <div className="flex items-center gap-1 ml-2">
              <History size={14} className="text-zinc-400" />
              <select
                onChange={(e) => onSelectRun(e.target.value)}
                value={runId && runs.some((r) => r.runId === runId) ? runId : ''}
                aria-label={t('pipeline.runHistory')}
                className="px-2 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
              >
                <option value="">{t('pipeline.history')}</option>
                {runs.map((r) => (
                  <option key={r.runId} value={r.runId}>
                    {r.status || t('pipeline.runLabel')}{r.startedUtc ? ` · ${r.startedUtc.slice(11, 19)}` : ''}
                  </option>
                ))}
              </select>
            </div>
          )}
          {dirty ? (
            <span className="text-xs text-amber-600">{t('pipeline.unsavedChanges')}</span>
          ) : savedId ? (
            <span className="text-xs text-green-600">{t('pipeline.saved')}</span>
          ) : null}
          {error && <span className="text-xs text-red-500 ml-2">{error}</span>}
        </div>
        {parameterKeys.length > 0 && (
          <div className="flex items-center gap-3 px-2 py-1 border-b border-zinc-200 dark:border-zinc-700 text-xs">
            <span className="text-zinc-500 flex items-center gap-1"><SlidersHorizontal size={12} /> {t('pipeline.parameters')}</span>
            {parameterKeys.map((k) => (
              <label key={k} className="flex items-center gap-1">
                <span className="font-mono text-zinc-600 dark:text-zinc-300">{k}</span>
                <input
                  value={runParameterValues[k] ?? ''}
                  onChange={(e) => setRunParameterValues((v) => ({ ...v, [k]: e.target.value }))}
                  className="w-28 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
                />
              </label>
            ))}
          </div>
        )}
        <div className="flex-1 relative">
          <ReactFlow
            nodes={canvasNodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeDragStart={onNodeDragStart}
            onNodesDelete={onNodesDelete}
            onEdgesDelete={onEdgesDelete}
            onNodeClick={(_, node) => { setSelectedNodeId(node.id); setSelectedEdgeId(null); }}
            onEdgeClick={(_, edge) => { setSelectedEdgeId(edge.id); setSelectedNodeId(null); }}
            onPaneClick={() => { setSelectedNodeId(null); setSelectedEdgeId(null); }}
            nodeTypes={nodeTypes}
            colorMode={theme}
            fitView
          >
            <Background />
            <Controls />
          </ReactFlow>
          {selectedNode && (
            <NodeInspector
              nodeId={selectedNode.id}
              data={selectedNode.data as unknown as PipelineNodeData}
              wiredInputs={wiredInputs}
              onSetPorts={setBoundaryPorts}
              onSetBinding={setBinding}
              onClose={() => setSelectedNodeId(null)}
            />
          )}
          {selectedEdge && (
            <WireInspector
              wireId={selectedEdge.id}
              fromPort={selectedEdge.sourceHandle ?? ''}
              toPort={selectedEdge.targetHandle ?? ''}
              paths={(selectedEdge.data as WirePaths | undefined) ?? {}}
              onSetPath={setEdgePath}
              onClose={() => setSelectedEdgeId(null)}
            />
          )}
          {nodes.length === 0 && (
            <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
              <div className="text-center text-sm text-zinc-500 dark:text-zinc-400 max-w-xs">
                {connections.length > 0 ? (
                  <>
                    <MousePointerClick className="mx-auto mb-2 text-zinc-400" size={28} />
                    <p className="font-medium text-zinc-600 dark:text-zinc-300">{t('pipeline.startTitle')}</p>
                    <p className="mt-1">{t('pipeline.startBody')}</p>
                  </>
                ) : (
                  <>
                    <p className="font-medium text-zinc-600 dark:text-zinc-300">{t('pipeline.noServicesTitle')}</p>
                    <p className="mt-1">{t('pipeline.noServicesBody')}</p>
                  </>
                )}
              </div>
            </div>
          )}
        </div>
        {(validationIssues.length > 0 || runId) && (
          <div className="border-t border-zinc-200 dark:border-zinc-700 p-2 text-xs max-h-40 overflow-auto">
            {validationIssues.length > 0 && (
              <ul aria-label={t('pipeline.findings')} className="mb-1 space-y-0.5">
                {validationIssues.map((issue, i) => (
                  <li key={i} className="flex items-start gap-1 text-amber-700 dark:text-amber-400">
                    <AlertTriangle size={12} className="mt-0.5 shrink-0" /> <span>{issue.message}</span>
                  </li>
                ))}
              </ul>
            )}
            {runId && (
              <>
                <div className={clsx('font-semibold', RUN_STATUS_COLOR[liveRunStatus ?? 'running'] ?? 'text-blue-600')}>
                  {t('pipeline.runStatus', { status: liveRunStatus ?? t('pipeline.starting') })}{runActive ? '…' : ''}
                </div>
                {nodes.map((n) => {
                  const d = n.data as unknown as PipelineNodeData;
                  return (
                    <div key={n.id} className="font-mono">
                      {t('pipeline.nodeStatus', { label: d.label, status: d.status ?? t('pipeline.pending') })}
                    </div>
                  );
                })}
              </>
            )}
          </div>
        )}
      </div>
      <OutputRail outputs={outputs} connections={connections} onPlaceOutput={placeOutput} onAddService={addNode} />
    </div>
  );
}
