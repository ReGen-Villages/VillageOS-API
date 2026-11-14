import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ReactFlow, Background, Controls, type NodeTypes } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { useModelStore } from '../stores/modelStore';
import { useThemeStore } from '../stores/themeStore';
import { useSubscription } from '../hooks/useSse';
import { usePipelineDrawing } from '../hooks/usePipelineDrawing';
import { usePipelineRun } from '../hooks/usePipelineRun';
import { usePipelineSelection } from '../hooks/usePipelineSelection';
import { PIPELINE_PAGE } from '../pipeline/subscription';
import { PipelineModel, ROLE } from '../pipeline/model';
import { takenOnOf } from '../pipeline/serialize';
import type { WireData } from '../pipeline/wireDrawing';
import { catalystRail, outputRail } from '../pipeline/catalysts';
import { PipelineNodeView, type PipelineNodeData } from '../components/pipeline/PipelineNodeView';
import { CatalystRail } from '../components/pipeline/CatalystRail';
import { OutputRail } from '../components/pipeline/OutputRail';
import { PipelineRoster } from '../components/pipeline/PipelineRoster';
import { NodeInspector } from '../components/pipeline/NodeInspector';
import { WireInspector } from '../components/pipeline/WireInspector';
import { PipelineToolbar } from '../components/pipeline/PipelineToolbar';
import { RunParametersBar } from '../components/pipeline/RunParametersBar';
import { EmptyCanvasHint } from '../components/pipeline/EmptyCanvasHint';
import { FindingsAndRunPanel } from '../components/pipeline/FindingsAndRunPanel';

const nodeTypes: NodeTypes = { pipelineNode: PipelineNodeView };

/**
 * Drawing the pipelines the orchestrator runs, around what sets each one off and what it leaves behind.
 *
 * A pipeline is model data — nodes, the connections they call, and the wires between their ports — so
 * this page is Thing and relationship work with a canvas over it. The rail on the left lists every
 * catalyst the model holds, the roster every pipeline, and the rail on the right every outcome and every
 * service a node may call. A boundary node stands for the catalyst or the outcome it was placed from.
 */
export function PipelinePage() {
  useSubscription(PIPELINE_PAGE);
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const loaded = useModelStore((s) => s.loaded);
  // The canvas paints its own controls and background and has to be told which lighting it is in.
  const theme = useThemeStore((s) => s.theme);
  const model = useMemo(() => new PipelineModel(things, relationships), [things, relationships]);
  const connections = useMemo(() => model.connections(), [model]);
  const pipelines = useMemo(
    () => model.thingsOf(ROLE.Pipeline)
      .map((pipeline) => ({
        id: pipeline.Id,
        name: pipeline.Name,
        nodeCount: model.outgoing(pipeline.Id, 'has').filter((held) => model.isOf(held.Id, ROLE.PipelineNode)).length,
      }))
      .sort((a, b) => a.name.localeCompare(b.name)),
    [model],
  );

  const [error, setError] = useState<string | null>(null);
  const drawing = usePipelineDrawing({ model, connections, setError });
  const { nodes, edges, setNodes, savedId, editingPipelineId, thingIdByCanvasId, currentNodes, load, save, startNew } = drawing;
  const run = usePipelineRun({ model, savedId, nodes, setNodes, thingIdByCanvasId, setError });
  const { forgetRun } = run;

  // Everything that sets a run off and everything one may leave behind, read again on every model flush,
  // because each group is a walk of the model.
  const catalysts = useMemo(() => catalystRail(model), [model]);
  const outputs = useMemo(() => outputRail(model, editingPipelineId), [model, editingPipelineId]);
  const selection = usePipelineSelection({ model, nodes, edges, outputs, currentNodes });
  const { selectedNode, selectedEdge, clearSelection } = selection;

  const openPipeline = useCallback((pipelineId: string) => {
    if (!load(pipelineId)) return;
    forgetRun();
    clearSelection();
  }, [load, forgetRun, clearSelection]);

  const onSave = useCallback(async () => {
    if (await save()) forgetRun();
  }, [save, forgetRun]);

  const onNew = useCallback((newName?: string) => {
    startNew(newName);
    forgetRun();
  }, [startNew, forgetRun]);

  // The first pipeline opens on arrival, once the model has loaded; a model holding none leaves the
  // empty canvas, where naming a new one still works.
  const openedOnArrival = useRef(false);
  useEffect(() => {
    if (openedOnArrival.current || !loaded || pipelines.length === 0) return;
    openedOnArrival.current = true;
    openPipeline(pipelines[0].id);
  }, [loaded, pipelines, openPipeline]);

  return (
    <div className="flex h-full flex-col md:flex-row">
      <CatalystRail groups={catalysts} onPlace={drawing.placeStart} onOpenPipeline={openPipeline} onHandOver={drawing.handOver} />
      <PipelineRoster pipelines={pipelines} openedPipelineId={editingPipelineId} onOpen={openPipeline} onCreate={onNew} />
      <div className="flex-1 min-w-0 min-h-0 flex flex-col">
        <PipelineToolbar
          name={drawing.name}
          onNameChange={drawing.setName}
          canUndo={drawing.canUndo}
          dirty={drawing.dirty}
          busy={drawing.busy}
          nodeCount={nodes.length}
          savedId={savedId}
          findings={drawing.validationIssues}
          runActive={run.runActive}
          runs={run.runs}
          runId={run.runId}
          chosenRunRequest={run.chosenRunRequest}
          error={error}
          onNew={onNew}
          onUndo={drawing.onUndo}
          onSave={onSave}
          onRun={run.onRun}
          onCancel={run.onCancel}
          onSelectRun={run.onSelectRun}
        />
        <RunParametersBar parameterKeys={run.parameterKeys} values={run.runParameterValues} onChange={run.setRunParameter} />
        <div className="flex-1 relative">
          <ReactFlow
            nodes={drawing.canvasNodes}
            edges={drawing.canvasEdges}
            onNodesChange={drawing.onNodesChange}
            onEdgesChange={drawing.onEdgesChange}
            onConnect={drawing.onConnect}
            onNodeDragStart={drawing.onNodeDragStart}
            onNodesDelete={drawing.onNodesDelete}
            onEdgesDelete={drawing.onEdgesDelete}
            onNodeClick={(_, node) => selection.selectNode(node.id)}
            onEdgeClick={(_, edge) => selection.selectEdge(edge.id)}
            onPaneClick={clearSelection}
            nodeTypes={nodeTypes}
            colorMode={theme}
            fitView
          >
            <Background />
            <Controls />
          </ReactFlow>
          {selectedNode && (
            <NodeInspector
              key={selectedNode.id}
              nodeId={selectedNode.id}
              data={selectedNode.data as unknown as PipelineNodeData}
              wiredInputs={selection.wiredInputs}
              offeredByPort={selection.offeredByPort}
              widerList={selection.widerList}
              onWiden={selection.setWidened}
              nameOf={selection.nameOf}
              standsForOptions={selection.standsForOptions}
              onSetStandsFor={drawing.setStandsFor}
              onSetPorts={drawing.setDeclaredPorts}
              onSetBinding={drawing.setBinding}
              onSetChosen={drawing.setChosen}
              onSetChoices={drawing.setChoices}
              onSetRule={drawing.setRule}
              onClose={selection.closeNodeInspector}
            />
          )}
          {selectedEdge && (
            <WireInspector
              wireId={selectedEdge.id}
              fromPortName={selection.portNameOf(selectedEdge.source, selectedEdge.sourceHandle)}
              toPortName={selection.portNameOf(selectedEdge.target, selectedEdge.targetHandle)}
              paths={(selectedEdge.data as WireData | undefined) ?? {}}
              onSetPath={drawing.setEdgePath}
              results={selection.resultsOf(selectedEdge.source)}
              takenOn={takenOnOf((selectedEdge.data as WireData | undefined)?.takenOn, model)}
              onSetTakenOn={drawing.setEdgeTakenOn}
              onClose={selection.closeWireInspector}
            />
          )}
          {nodes.length === 0 && <EmptyCanvasHint hasServices={connections.length > 0} />}
        </div>
        <FindingsAndRunPanel
          findings={drawing.validationIssues}
          runId={run.runId}
          liveRunStatus={run.liveRunStatus}
          runActive={run.runActive}
          nodes={nodes}
        />
      </div>
      <OutputRail outputs={outputs} connections={connections} onPlaceOutput={drawing.placeOutput} onAddService={drawing.addNode} />
    </div>
  );
}
