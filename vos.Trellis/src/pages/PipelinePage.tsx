import { useCallback, useMemo, useState } from 'react';
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
import { Play, Save, FolderOpen, FilePlus, MousePointerClick } from 'lucide-react';
import { useModelStore } from '../stores/modelStore';
import { PipelineModel, ARCHETYPE, typesCompatible, type ConnectionInfo } from '../pipeline/model';
import { savePipeline, loadPipeline, type EditorNode, type EditorEdge } from '../pipeline/serialize';
import { pipelineApi, type PipelineRunResult } from '../api/pipelineApi';
import { PipelineNodeView, type PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import { Palette } from '../components/pipeline/Palette';

const nodeTypes: NodeTypes = { pipelineNode: PipelineNodeView };

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
  const [result, setResult] = useState<PipelineRunResult | null>(null);

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
      const id = await savePipeline(name, toEditorNodes(), toEditorEdges(), model);
      setSavedId(id);
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
    setResult(null);
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
    setResult(null);
    setError(null);
  }, [setNodes, setEdges]);

  const onRun = useCallback(async () => {
    if (!savedId) return;
    setBusy(true);
    setError(null);
    try {
      setResult(await pipelineApi.spawn(savedId));
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Run failed.');
    } finally {
      setBusy(false);
    }
  }, [savedId]);

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
          <button onClick={onRun} disabled={busy || !savedId} className="flex items-center gap-1 px-3 py-1 text-sm rounded bg-green-600 text-white disabled:opacity-50">
            <Play size={14} /> Run
          </button>
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
        {result && (
          <div className="border-t border-zinc-200 dark:border-zinc-700 p-2 text-xs max-h-40 overflow-auto">
            <div className={result.success ? 'text-green-600 font-semibold' : 'text-red-500 font-semibold'}>
              Run {result.success ? 'succeeded' : 'failed'}{result.error ? `: ${result.error}` : ''}
            </div>
            {result.nodes.map((n) => (
              <div key={n.nodeId} className="font-mono">
                {n.name}: {n.status}{n.error ? ` — ${n.error}` : ''}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
