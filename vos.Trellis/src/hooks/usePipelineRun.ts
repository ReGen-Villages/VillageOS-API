import { useCallback, useEffect, useMemo, useState, type Dispatch, type SetStateAction } from 'react';
import { useTranslation } from 'react-i18next';
import type { Node } from '@xyflow/react';
import { parseParameterValue } from '../pages/parseParamValue';
import { PipelineModel } from '../pipeline/model';
import { stopRun } from '../pipeline/stop';
import { pipelineApi } from '../api/pipelineApi';
import type { PipelineNodeData } from '../components/pipeline/PipelineNodeView';

interface PipelineRunInputs {
  model: PipelineModel;
  savedId: string | null;
  nodes: Node[];
  setNodes: Dispatch<SetStateAction<Node[]>>;
  thingIdByCanvasId: Record<string, string>;
  setError: (error: string | null) => void;
}

export function usePipelineRun({ model, savedId, nodes, setNodes, thingIdByCanvasId, setError }: PipelineRunInputs) {
  const { t } = useTranslation();
  const [runId, setRunId] = useState<string | null>(null);
  const [runParameterValues, setRunParameterValues] = useState<Record<string, string>>({});

  const parameterKeys = useMemo(() => {
    const keys = new Set<string>();
    for (const n of nodes) {
      const b = (n.data as unknown as PipelineNodeData).parameterBindings;
      if (b) for (const k of Object.values(b)) keys.add(k);
    }
    return [...keys].sort();
  }, [nodes]);

  const liveRunStatus = runId ? model.runStatus(runId) : undefined;
  const runActive = !!runId && (liveRunStatus === undefined || ['queued', 'running', 'waiting', 'trying'].includes(liveRunStatus));
  const runs = useMemo(() => (savedId ? model.runsOf(savedId) : []), [savedId, model]);
  const chosenRunRequest = runs.find((r) => r.runId === runId)?.requestId;

  const clearStatuses = useCallback(() => {
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
  }, [setNodes]);

  const onSelectRun = useCallback((id: string) => {
    clearStatuses();
    setRunId(id || null);
  }, [clearStatuses]);

  const forgetRun = useCallback(() => setRunId(null), []);

  const setRunParameter = useCallback((key: string, value: string) => {
    setRunParameterValues((v) => ({ ...v, [key]: value }));
  }, []);

  const onRun = useCallback(async () => {
    if (!savedId) return;
    setError(null);
    setNodes((ns) => ns.map((n) => ({ ...n, data: { ...n.data, status: undefined, progress: undefined } })));
    try {
      // Started without waiting: the run's identifier comes back at once, and the effect below lights up the
      // nodes as the run goes. A parameter that reads as JSON is passed as that value, so a list such as
      // `["a","b"]` runs once per item and `42` is a number; anything else is passed as text.
      const parameters = Object.fromEntries(parameterKeys.map((k) => [k, parseParameterValue(runParameterValues[k] ?? '')]));
      const accepted = await pipelineApi.spawnAsync(savedId, parameters);
      setRunId(accepted.runId);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.runFailed'));
    }
  }, [savedId, setNodes, parameterKeys, runParameterValues, setError, t]);

  const onCancel = useCallback(async () => {
    if (!runId) return;
    try {
      await stopRun(runId, t('pipeline.cancelledFromThePage'), model);
    } catch (e) {
      setError(e instanceof Error ? e.message : t('pipeline.cancelFailed'));
    }
  }, [runId, setError, t, model]);

  // As Phloem writes each node run's status, the model store updates from the broker's event stream and this
  // paints each node's status ring (PipelineNodeView). A node run is keyed by its Thing's identifier, which the
  // save and load map ties to its node on the canvas.
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

  return {
    runId, liveRunStatus, runActive, runs, chosenRunRequest, parameterKeys, runParameterValues,
    setRunParameter, onSelectRun, onRun, onCancel, forgetRun,
  };
}
