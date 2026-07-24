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
import { Play, Save, FolderOpen, FilePlus, MousePointerClick, Ban, History, SlidersHorizontal, AlertTriangle, Undo2 } from 'lucide-react';
import { useModelStore } from '../stores/modelStore';
import { PipelineModel, ARCHETYPE, typesCompatible, type ConnectionInfo, type PortInfo } from '../pipeline/model';
import { savePipeline, loadPipeline, type EditorNode, type EditorEdge } from '../pipeline/serialize';
import { validatePipeline } from '../pipeline/validate';
import { EditorHistory } from '../pipeline/history';
import { pipelineApi } from '../api/pipelineApi';
import { PipelineNodeView, type PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import { Palette } from '../components/pipeline/Palette';

const nodeTypes: NodeTypes = { pipelineNode: PipelineNodeView };

const RUN_STATUS_COLOR: Record<string, string> = {
  running: 'text-blue-600',
  succeeded: 'text-green-600',
  failed: 'text-red-500',
  cancelled: 'text-amber-600',
  partial: 'text-orange-500',
};

let nodeSeq = 0;

/** Parse a Params-bar value as JSON when it is valid JSON (lists, numbers, booleans, objects); otherwise keep
 * it as the raw string. Lets a user type `["a","b","c"]` to drive a fan-out, or `42` for a number param. */
export function parseParamValue(raw: string): unknown {
  const trimmed = raw.trim();
  if (trimmed === '') return '';
  try {
    return JSON.parse(trimmed);
  } catch {
    return raw;
  }
}

/** A short edge label for a mapped wire (#5874/#5875), e.g. `user.id → a` with a trailing `ƒ` when the wire
 * carries a JSONata transform; undefined when the wire is a plain whole-payload pass-through. */
function pathLabel(fromPath?: string, toPath?: string, transform?: string): string | undefined {
  if (!fromPath && !toPath && !transform) return undefined;
  const paths = fromPath || toPath ? `${fromPath || '·'} → ${toPath || '·'}` : '';
  return transform ? `${paths} ƒ`.trim() : paths;
}

export function PipelinePage() {
  const { t } = useTranslation();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const model = useMemo(() => new PipelineModel(things, relationships), [things, relationships]);
  const connections = useMemo(() => model.connections(), [model]);
  const pipelines = useMemo(
    () => things.filter((t) => model.isOfType(t.Id, ARCHETYPE.Pipeline)),
    [things, model],
  );

  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [name, setName] = useState(() => t('pipeline.newPipelineName'));
  const [savedId, setSavedId] = useState<string | null>(null);
  // The persistent id of the pipeline being edited — kept across edits (which clear savedId to mark the
  // canvas dirty) so a save UPDATES the loaded pipeline in place instead of forking a duplicate (#5826).
  const [editingPipelineId, setEditingPipelineId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Live run: the active run id + the canvas-node-id → Thing-id map so NodeRun statuses (keyed by Thing id)
  // land on the right canvas node as they stream in over SSE.
  const [runId, setRunId] = useState<string | null>(null);
  const [thingIdByCanvasId, setThingIdByCanvasId] = useState<Record<string, string>>({});
  // Param routing (#5647): the selected node (binding editor) + the values supplied for each bound run param.
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedEdgeId, setSelectedEdgeId] = useState<string | null>(null);
  const [runParamValues, setRunParamValues] = useState<Record<string, string>>({});

  // Undo + optimistic rollback (#5872). The history records the editor state *before* each mutation (undo),
  // and holds the last server-confirmed state as a baseline (rollback on a rejected save). `canUndo`/`dirty`
  // drive the toolbar. A ref mirrors the latest nodes/edges so any handler can snapshot the current state.
  type EditorSnapshot = { nodes: Node[]; edges: Edge[] };
  const historyRef = useRef(new EditorHistory<EditorSnapshot>({ nodes: [], edges: [] }));
  const stateRef = useRef<EditorSnapshot>({ nodes, edges });
  stateRef.current = { nodes, edges };
  const [canUndo, setCanUndo] = useState(false);
  const [dirty, setDirty] = useState(false);

  const recordSnapshot = useCallback(() => {
    historyRef.current.record(structuredClone(stateRef.current));
    setCanUndo(true);
    setDirty(true);
  }, []);

  const applySnapshot = useCallback((s: EditorSnapshot) => {
    setNodes(structuredClone(s.nodes));
    setEdges(structuredClone(s.edges));
  }, [setNodes, setEdges]);

  // Baseline = the last server-confirmed state; also clears the undo stack (a fresh save/load/new is the floor).
  const commitBaseline = useCallback((s: EditorSnapshot) => {
    historyRef.current.commit(structuredClone(s));
    setCanUndo(false);
    setDirty(false);
  }, []);

  const onUndo = useCallback(() => {
    const prev = historyRef.current.undo();
    if (!prev) return;
    applySnapshot(prev);
    setCanUndo(historyRef.current.canUndo());
    setDirty(true);
    setSavedId(null); // an undo leaves the canvas out of step with the last save
  }, [applySnapshot]);

  // The distinct run-param keys any node binds an input to — drives the Params form.
  const paramKeys = useMemo(() => {
    const keys = new Set<string>();
    for (const n of nodes) {
      const b = (n.data as unknown as PipelineNodeData).paramBindings;
      if (b) for (const k of Object.values(b)) keys.add(k);
    }
    return [...keys].sort();
  }, [nodes]);

  // Pre-run validation (#5829): why the DAG will not run — required inputs neither wired nor param-bound,
  // and dangling wires. Surfaced in the toolbar and gates Run so a broken pipeline fails loud, not silent.
  const validationIssues = useMemo(() => {
    const valNodes = nodes.map((n) => {
      const d = n.data as unknown as PipelineNodeData;
      return { id: n.id, label: d.label, ports: d.ports, paramBindings: d.paramBindings };
    });
    const valEdges = edges.map((e) => ({
      source: e.source,
      sourceHandle: e.sourceHandle ?? '',
      target: e.target,
      targetHandle: e.targetHandle ?? '',
    }));
    return validatePipeline(valNodes, valEdges);
  }, [nodes, edges]);

  const selectedNode = selectedNodeId ? nodes.find((n) => n.id === selectedNodeId) : undefined;
  const selectedEdge = selectedEdgeId ? edges.find((e) => e.id === selectedEdgeId) : undefined;

  // Edit a wire's field-path (#5874): update the edge's data + its label, and mark the canvas dirty.
  const setEdgePath = useCallback((edgeId: string, which: 'fromPath' | 'toPath' | 'transform', value: string) => {
    recordSnapshot();
    setEdges((es) => es.map((e) => {
      if (e.id !== edgeId) return e;
      const data = { ...(e.data as { fromPath?: string; toPath?: string; transform?: string } | undefined), [which]: value || undefined };
      return { ...e, data, label: pathLabel(data.fromPath, data.toPath, data.transform) };
    }));
    setSavedId(null);
  }, [setEdges, recordSnapshot]);

  const liveRunStatus = runId ? model.runStatus(runId) : undefined;
  const runActive = !!runId && (liveRunStatus === undefined || liveRunStatus === 'running');
  // Past + in-flight runs of the loaded pipeline — the history panel (#5646).
  const runs = useMemo(() => (savedId ? model.runsOf(savedId) : []), [savedId, model]);

  const clearStatuses = useCallback(() => {
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
  }, [setNodes]);

  // Replay a past run: point runId at it and the animation effect paints its (terminal) node statuses.
  const onSelectRun = useCallback((id: string) => {
    clearStatuses();
    setRunId(id || null);
  }, [clearStatuses]);

  const addNode = useCallback((c: ConnectionInfo) => {
    recordSnapshot();
    const data: PipelineNodeData = { label: c.name, connectionId: c.connectionId, subdomain: c.subdomain, ports: c.ports };
    setNodes((ns) => [
      ...ns,
      {
        id: `n${++nodeSeq}`,
        type: 'pipelineNode',
        position: { x: 80 + ns.length * 60, y: 80 + ns.length * 40 },
        data: data as unknown as Record<string, unknown>,
      },
    ]);
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  // Drop a boundary node (#5873): an Input source (one output port) or an Output sink (one input port). Its
  // ports are user-declared — editable in the inspector — and the run fills an Input's outputs from the run
  // parameters and collects an Output's inputs as the pipeline result.
  const addBoundaryNode = useCallback((kind: 'input' | 'output') => {
    recordSnapshot();
    const direction = kind === 'input' ? 'out' : 'in';
    const data: PipelineNodeData = {
      label: kind === 'input' ? t('pipeline.boundaryInput') : t('pipeline.boundaryOutput'),
      kind,
      ports: [{ portName: 'value', direction, type: 'any', required: kind === 'output' }],
    };
    setNodes((ns) => [
      ...ns,
      {
        id: `n${++nodeSeq}`,
        type: 'pipelineNode',
        position: { x: 80 + ns.length * 60, y: 80 + ns.length * 40 },
        data: data as unknown as Record<string, unknown>,
      },
    ]);
    setSavedId(null);
  }, [setNodes, recordSnapshot, t]);

  // Add / rename / remove a port on a boundary node (its ports are user-declared). Direction is fixed by the
  // node kind (Input → output ports, Output → input ports).
  const setBoundaryPorts = useCallback((nodeId: string, ports: PortInfo[]) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => (n.id === nodeId ? { ...n, data: { ...n.data, ports } } : n)));
    setSavedId(null);
  }, [setNodes, recordSnapshot]);

  // Type-check a wire before accepting it (out-port type must be compatible with in-port type).
  const onConnect = useCallback((c: Connection) => {
    const src = nodes.find((n) => n.id === c.source)?.data as PipelineNodeData | undefined;
    const tgt = nodes.find((n) => n.id === c.target)?.data as PipelineNodeData | undefined;
    const outPort = src?.ports.find((p) => p.portName === c.sourceHandle && p.direction === 'out');
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
      return { id: n.id, connectionId: d.connectionId ?? '', label: d.label, x: n.position.x, y: n.position.y, ports: d.ports, paramBindings: d.paramBindings, kind: d.kind };
    });

  const toEditorEdges = (): EditorEdge[] =>
    edges.map((e) => ({
      id: e.id,
      source: e.source,
      sourceHandle: e.sourceHandle ?? '',
      target: e.target,
      targetHandle: e.targetHandle ?? '',
      fromPath: (e.data as { fromPath?: string } | undefined)?.fromPath,
      transform: (e.data as { transform?: string } | undefined)?.transform,
      toPath: (e.data as { toPath?: string } | undefined)?.toPath,
    }));

  const onSave = useCallback(async () => {
    // Optimistic rollback (#5872): remember the state we are trying to save so a rejected save can revert
    // the canvas to the last server-confirmed state instead of leaving it out of step with the server.
    const attempt = structuredClone(stateRef.current);
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
      const target = historyRef.current.rollbackTarget();
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
    const loaded = loadPipeline(pipelineId, model);
    if (!loaded) return;
    setName(loaded.name);
    setSavedId(pipelineId);
    setEditingPipelineId(pipelineId);
    setRunId(null);
    // Loaded canvas node ids ARE the Thing ids, so the canvas→Thing map is the identity.
    setThingIdByCanvasId(Object.fromEntries(loaded.nodes.map((n) => [n.id, n.id])));
    const loadedNodes: Node[] = loaded.nodes.map((n) => ({
      id: n.id,
      type: 'pipelineNode',
      position: { x: n.x, y: n.y },
      data: { label: n.label, kind: n.kind, connectionId: n.connectionId, subdomain: connections.find((c) => c.connectionId === n.connectionId)?.subdomain ?? '', ports: n.ports, paramBindings: n.paramBindings } as unknown as Record<string, unknown>,
    }));
    const loadedEdges: Edge[] = loaded.edges.map((e) => ({ id: e.id, source: e.source, sourceHandle: e.sourceHandle, target: e.target, targetHandle: e.targetHandle, label: pathLabel(e.fromPath, e.toPath, e.transform), data: { fromPath: e.fromPath, toPath: e.toPath, transform: e.transform } }));
    setNodes(loadedNodes);
    setEdges(loadedEdges);
    commitBaseline({ nodes: loadedNodes, edges: loadedEdges }); // a freshly loaded pipeline is the undo/rollback floor
  }, [model, connections, setNodes, setEdges, commitBaseline]);

  const onNew = useCallback(() => {
    setNodes([]);
    setEdges([]);
    setName(t('pipeline.newPipelineName'));
    setSavedId(null);
    setEditingPipelineId(null);
    setRunId(null);
    setThingIdByCanvasId({});
    setError(null);
    commitBaseline({ nodes: [], edges: [] }); // empty canvas is the floor; nothing to undo/roll back to
  }, [setNodes, setEdges, commitBaseline, t]);

  const onRun = useCallback(async () => {
    if (!savedId) return;
    setError(null);
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
    try {
      // Async spawn — get the run id up front and let the SSE animation effect below light up nodes.
      // Param values are parsed as JSON when valid (so a list `["a","b"]` drives fan-out, `42`→number),
      // otherwise passed through as a plain string.
      const params = Object.fromEntries(paramKeys.map((k) => [k, parseParamValue(runParamValues[k] ?? '')]));
      const accepted = await pipelineApi.spawnAsync(savedId, params);
      setRunId(accepted.runId);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.runFailed'));
    }
  }, [savedId, setNodes, paramKeys, runParamValues, t]);

  // Bind (or clear) an input port of a node to a run-param key.
  const setBinding = useCallback((nodeId: string, port: string, paramKey: string) => {
    recordSnapshot();
    setNodes((ns) => ns.map((n) => {
      if (n.id !== nodeId) return n;
      const d = n.data as unknown as PipelineNodeData;
      const next = { ...(d.paramBindings ?? {}) };
      if (paramKey.trim()) next[port] = paramKey.trim();
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

  return (
    <div className="flex h-full">
      <Palette connections={connections} onAdd={addNode} onAddBoundary={addBoundaryNode} />
      <div className="flex-1 flex flex-col">
        <div className="flex items-center gap-2 p-2 border-b border-zinc-200 dark:border-zinc-700">
          <button onClick={onNew} className="flex items-center gap-1 px-3 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 hover:border-blue-400">
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
          {validationIssues.length > 0 && (
            <span
              className="flex items-center gap-1 text-xs text-amber-600"
              title={validationIssues.map((i) => i.message).join('\n')}
            >
              <AlertTriangle size={12} /> {t('pipeline.issues', { count: validationIssues.length })}
            </span>
          )}
          <div className="flex items-center gap-1 ml-2">
            <FolderOpen size={14} className="text-zinc-400" />
            <select
              onChange={(e) => e.target.value && onLoad(e.target.value)}
              value={savedId ?? ''}
              className="px-2 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
            >
              <option value="">{t('pipeline.loadPipeline')}</option>
              {pipelines.map((p) => <option key={p.Id} value={p.Id}>{p.Name}</option>)}
            </select>
          </div>
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
        {paramKeys.length > 0 && (
          <div className="flex items-center gap-3 px-2 py-1 border-b border-zinc-200 dark:border-zinc-700 text-xs">
            <span className="text-zinc-500 flex items-center gap-1"><SlidersHorizontal size={12} /> {t('pipeline.params')}</span>
            {paramKeys.map((k) => (
              <label key={k} className="flex items-center gap-1">
                <span className="font-mono text-zinc-600 dark:text-zinc-300">{k}</span>
                <input
                  value={runParamValues[k] ?? ''}
                  onChange={(e) => setRunParamValues((v) => ({ ...v, [k]: e.target.value }))}
                  className="w-28 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
                />
              </label>
            ))}
          </div>
        )}
        <div className="flex-1 relative">
          <ReactFlow
            nodes={nodes}
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
            fitView
          >
            <Background />
            <Controls />
          </ReactFlow>
          {selectedNode && (() => {
            const d = selectedNode.data as unknown as PipelineNodeData;
            const inputs = d.ports.filter((p) => p.direction === 'in');
            const wired = new Set(edges.filter((e) => e.target === selectedNode.id).map((e) => e.targetHandle));
            const bindings = d.paramBindings ?? {};
            return (
              <div className="absolute top-2 right-2 w-64 bg-white dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-600 rounded shadow-lg p-2 text-xs z-10">
                <div className="font-semibold mb-1.5 flex items-center justify-between gap-2">
                  <span className="truncate">{d.label}</span>
                  <button onClick={() => setSelectedNodeId(null)} aria-label={t('pipeline.closeInspector')} className="text-zinc-400 hover:text-zinc-600">×</button>
                </div>
                {d.kind ? (
                  <div className="flex flex-col gap-1">
                    <div className="text-zinc-400">{t('pipeline.ports', { direction: d.kind === 'input' ? t('pipeline.outputs') : t('pipeline.inputs') })}</div>
                    {d.ports.map((p, i) => (
                      <div key={i} className="flex items-center gap-1">
                        <input
                          aria-label={t('pipeline.portName', { index: i + 1 })}
                          value={p.portName}
                          onChange={(e) => setBoundaryPorts(selectedNode.id, d.ports.map((q, j) => (j === i ? { ...q, portName: e.target.value } : q)))}
                          className="flex-1 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900"
                        />
                        <button
                          onClick={() => setBoundaryPorts(selectedNode.id, d.ports.filter((_, j) => j !== i))}
                          aria-label={t('pipeline.removePort', { name: p.portName })}
                          className="text-zinc-400 hover:text-red-400 px-1"
                        >×</button>
                      </div>
                    ))}
                    <button
                      onClick={() => setBoundaryPorts(selectedNode.id, [...d.ports, { portName: `port${d.ports.length + 1}`, direction: d.kind === 'input' ? 'out' : 'in', type: 'any', required: d.kind === 'output' }])}
                      className="mt-1 text-blue-500 hover:text-blue-600 text-left"
                    >{t('pipeline.addPort')}</button>
                  </div>
                ) : inputs.length === 0 ? (
                  <div className="text-zinc-400">{t('pipeline.noInputPorts')}</div>
                ) : (
                  <div className="flex flex-col gap-1">
                    <div className="text-zinc-400">{t('pipeline.bindInput')}</div>
                    {inputs.map((p) => (
                      <label key={p.portName} className="flex items-center gap-1">
                        <span className="w-16 truncate text-zinc-600 dark:text-zinc-300">{p.portName}</span>
                        {wired.has(p.portName) ? (
                          <span className="flex-1 italic text-zinc-400">{t('pipeline.wired')}</span>
                        ) : (
                          <input
                            placeholder={t('pipeline.fromParam')}
                            value={bindings[p.portName] ?? ''}
                            onChange={(e) => setBinding(selectedNode.id, p.portName, e.target.value)}
                            className="flex-1 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900"
                          />
                        )}
                      </label>
                    ))}
                  </div>
                )}
              </div>
            );
          })()}
          {selectedEdge && (() => {
            const data = (selectedEdge.data as { fromPath?: string; toPath?: string; transform?: string } | undefined) ?? {};
            return (
              <div className="absolute top-2 right-2 w-64 bg-white dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-600 rounded shadow-lg p-2 text-xs z-10">
                <div className="font-semibold mb-1.5 flex items-center justify-between gap-2">
                  <span className="truncate">{t('pipeline.wireLabel', { from: selectedEdge.sourceHandle, to: selectedEdge.targetHandle })}</span>
                  <button onClick={() => setSelectedEdgeId(null)} aria-label={t('pipeline.closeInspector')} className="text-zinc-400 hover:text-zinc-600">×</button>
                </div>
                <div className="text-zinc-400 mb-1">{t('pipeline.mapField')}</div>
                <label className="flex items-center gap-1 mb-1">
                  <span className="w-16 truncate text-zinc-600 dark:text-zinc-300">{t('pipeline.fromPath')}</span>
                  <input
                    aria-label={t('pipeline.wireFromPath')}
                    placeholder="e.g. user.id"
                    value={data.fromPath ?? ''}
                    onChange={(e) => setEdgePath(selectedEdge.id, 'fromPath', e.target.value)}
                    className="flex-1 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900"
                  />
                </label>
                <label className="flex items-center gap-1">
                  <span className="w-16 truncate text-zinc-600 dark:text-zinc-300">{t('pipeline.toPath')}</span>
                  <input
                    aria-label={t('pipeline.wireToPath')}
                    placeholder="e.g. a"
                    value={data.toPath ?? ''}
                    onChange={(e) => setEdgePath(selectedEdge.id, 'toPath', e.target.value)}
                    className="flex-1 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900"
                  />
                </label>
                <div className="text-zinc-400 mt-2 mb-1">{t('pipeline.transformLabel')}</div>
                <textarea
                  aria-label={t('pipeline.wireTransform')}
                  placeholder='e.g. {"name": firstName & " " & lastName}'
                  value={data.transform ?? ''}
                  onChange={(e) => setEdgePath(selectedEdge.id, 'transform', e.target.value)}
                  rows={2}
                  className="w-full px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 font-mono"
                />
              </div>
            );
          })()}
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
        {runId && (
          <div className="border-t border-zinc-200 dark:border-zinc-700 p-2 text-xs max-h-40 overflow-auto">
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
          </div>
        )}
      </div>
    </div>
  );
}
