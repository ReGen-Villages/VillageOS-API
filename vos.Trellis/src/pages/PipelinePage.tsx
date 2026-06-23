import { useCallback, useEffect, useMemo, useState } from 'react';
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
import { Play, Save, FolderOpen, FilePlus, MousePointerClick, Ban } from 'lucide-react';
import { useModelStore } from '../stores/modelStore';
import { PipelineModel, ARCHETYPE, typesCompatible, type ConnectionInfo } from '../pipeline/model';
import { savePipeline, loadPipeline, type EditorNode, type EditorEdge } from '../pipeline/serialize';
import { pipelineApi } from '../api/pipelineApi';
import { PipelineNodeView, type PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import { Palette } from '../components/pipeline/Palette';

const nodeTypes: NodeTypes = { pipelineNode: PipelineNodeView };

const RUN_STATUS_COLOR: Record<string, string> = {
  running: 'text-blue-600',
  succeeded: 'text-green-600',
  failed: 'text-red-500',
  cancelled: 'text-amber-600',
};

let nodeSeq = 0;

export function PipelinePage() {
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
  const [name, setName] = useState('New Pipeline');
  const [savedId, setSavedId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Live run: the active run id + the canvas-node-id → Thing-id map so NodeRun statuses (keyed by Thing id)
  // land on the right canvas node as they stream in over SSE.
  const [runId, setRunId] = useState<string | null>(null);
  const [thingIdByCanvasId, setThingIdByCanvasId] = useState<Record<string, string>>({});

  const liveRunStatus = runId ? model.runStatus(runId) : undefined;
  const runActive = !!runId && (liveRunStatus === undefined || liveRunStatus === 'running');

  const addNode = useCallback((c: ConnectionInfo) => {
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
  }, [setNodes]);

  // Type-check a wire before accepting it (out-port type must be compatible with in-port type).
  const onConnect = useCallback((c: Connection) => {
    const src = nodes.find((n) => n.id === c.source)?.data as PipelineNodeData | undefined;
    const tgt = nodes.find((n) => n.id === c.target)?.data as PipelineNodeData | undefined;
    const outPort = src?.ports.find((p) => p.portName === c.sourceHandle && p.direction === 'out');
    const inPort = tgt?.ports.find((p) => p.portName === c.targetHandle && p.direction === 'in');
    if (!outPort || !inPort) return;
    if (!typesCompatible(outPort.type, inPort.type)) {
      setError(`Incompatible wire: ${outPort.type || 'any'} → ${inPort.type || 'any'}`);
      return;
    }
    setError(null);
    setEdges((es) => addEdge(c, es));
    setSavedId(null);
  }, [nodes, setEdges]);

  const toEditorNodes = (): EditorNode[] =>
    nodes.map((n) => {
      const d = n.data as unknown as PipelineNodeData;
      return { id: n.id, connectionId: d.connectionId, label: d.label, x: n.position.x, y: n.position.y, ports: d.ports };
    });

  const toEditorEdges = (): EditorEdge[] =>
    edges.map((e) => ({
      id: e.id,
      source: e.source,
      sourceHandle: e.sourceHandle ?? '',
      target: e.target,
      targetHandle: e.targetHandle ?? '',
    }));

  const onSave = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      const saved = await savePipeline(name, toEditorNodes(), toEditorEdges(), model);
      setSavedId(saved.pipelineId);
      setThingIdByCanvasId(saved.nodeIdMap);
      setRunId(null);
      setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined } })));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.');
    } finally {
      setBusy(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [name, nodes, edges, model]);

  const onLoad = useCallback((pipelineId: string) => {
    const loaded = loadPipeline(pipelineId, model);
    if (!loaded) return;
    setName(loaded.name);
    setSavedId(pipelineId);
    setRunId(null);
    // Loaded canvas node ids ARE the Thing ids, so the canvas→Thing map is the identity.
    setThingIdByCanvasId(Object.fromEntries(loaded.nodes.map((n) => [n.id, n.id])));
    setNodes(loaded.nodes.map((n) => ({
      id: n.id,
      type: 'pipelineNode',
      position: { x: n.x, y: n.y },
      data: { label: n.label, connectionId: n.connectionId, subdomain: connections.find((c) => c.connectionId === n.connectionId)?.subdomain ?? '', ports: n.ports } as unknown as Record<string, unknown>,
    })));
    setEdges(loaded.edges.map((e) => ({ id: e.id, source: e.source, sourceHandle: e.sourceHandle, target: e.target, targetHandle: e.targetHandle })));
  }, [model, connections, setNodes, setEdges]);

  const onNew = useCallback(() => {
    setNodes([]);
    setEdges([]);
    setName('New Pipeline');
    setSavedId(null);
    setRunId(null);
    setThingIdByCanvasId({});
    setError(null);
  }, [setNodes, setEdges]);

  const onRun = useCallback(async () => {
    if (!savedId) return;
    setError(null);
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined } })));
    try {
      // Async spawn — get the run id up front and let the SSE animation effect below light up nodes.
      const accepted = await pipelineApi.spawnAsync(savedId);
      setRunId(accepted.runId);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Run failed.');
    }
  }, [savedId, setNodes]);

  const onCancel = useCallback(async () => {
    if (!runId) return;
    try {
      await pipelineApi.cancel(runId);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Cancel failed.');
    }
  }, [runId]);

  // Drive the live animation: as Phloem writes NodeRun statuses, the model store updates over SSE → this
  // recomputes and paints each node's status ring (PipelineNodeView). Canvas ids map to Thing ids via the
  // save/load map so each NodeRun (keyed by Thing id) finds its node.
  useEffect(() => {
    if (!runId) return;
    const statuses = model.nodeRunStatuses(runId);
    if (Object.keys(statuses).length === 0) return;
    setNodes((ns) => ns.map((n) => {
      const thingId = thingIdByCanvasId[n.id] ?? n.id;
      const status = statuses[thingId];
      return status !== undefined ? { ...n, data: { ...n.data, status } } : n;
    }));
  }, [runId, model, thingIdByCanvasId, setNodes]);

  return (
    <div className="flex h-full">
      <Palette connections={connections} onAdd={addNode} />
      <div className="flex-1 flex flex-col">
        <div className="flex items-center gap-2 p-2 border-b border-zinc-200 dark:border-zinc-700">
          <button onClick={onNew} className="flex items-center gap-1 px-3 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 hover:border-blue-400">
            <FilePlus size={14} /> New
          </button>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            aria-label="Pipeline name"
            className="px-2 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
          />
          <button onClick={onSave} disabled={busy || nodes.length === 0} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-blue-600 text-white disabled:opacity-50">
            <Save size={14} /> Save
          </button>
          {runActive ? (
            <button onClick={onCancel} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-amber-600 text-white">
              <Ban size={14} /> Cancel
            </button>
          ) : (
            <button onClick={onRun} disabled={busy || !savedId} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-green-600 text-white disabled:opacity-50">
              <Play size={14} /> Run
            </button>
          )}
          <div className="flex items-center gap-1 ml-2">
            <FolderOpen size={14} className="text-zinc-400" />
            <select
              onChange={(e) => e.target.value && onLoad(e.target.value)}
              value={savedId ?? ''}
              className="px-2 py-1 text-sm rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
            >
              <option value="">Load pipeline…</option>
              {pipelines.map((p) => <option key={p.Id} value={p.Id}>{p.Name}</option>)}
            </select>
          </div>
          {savedId && <span className="text-xs text-green-600">saved</span>}
          {error && <span className="text-xs text-red-500 ml-2">{error}</span>}
        </div>
        <div className="flex-1 relative">
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            nodeTypes={nodeTypes}
            fitView
          >
            <Background />
            <Controls />
          </ReactFlow>
          {nodes.length === 0 && (
            <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
              <div className="text-center text-sm text-zinc-500 dark:text-zinc-400 max-w-xs">
                {connections.length > 0 ? (
                  <>
                    <MousePointerClick className="mx-auto mb-2 text-zinc-400" size={28} />
                    <p className="font-medium text-zinc-600 dark:text-zinc-300">Start a new pipeline</p>
                    <p className="mt-1">Click a service in the <span className="font-medium">Services</span> palette on the left to drop your first node, then drag between ports to wire them.</p>
                  </>
                ) : (
                  <>
                    <p className="font-medium text-zinc-600 dark:text-zinc-300">No services available</p>
                    <p className="mt-1">Load a model that has registered <span className="font-medium">Connections</span> (e.g. the pipeline demo seed) — they appear in the palette as nodes you can add.</p>
                  </>
                )}
              </div>
            </div>
          )}
        </div>
        {runId && (
          <div className="border-t border-zinc-200 dark:border-zinc-700 p-2 text-xs max-h-40 overflow-auto">
            <div className={clsx('font-semibold', RUN_STATUS_COLOR[liveRunStatus ?? 'running'] ?? 'text-blue-600')}>
              Run {liveRunStatus ?? 'starting'}{runActive ? '…' : ''}
            </div>
            {nodes.map((n) => {
              const d = n.data as unknown as PipelineNodeData;
              return (
                <div key={n.id} className="font-mono">
                  {d.label}: {d.status ?? 'pending'}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
