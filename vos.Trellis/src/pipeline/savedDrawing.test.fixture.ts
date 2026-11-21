/// <reference types="node" />
import { vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
import { apiClient } from '../api/client';
import { usePipelineDrawing } from '../hooks/usePipelineDrawing';
import type { OutputRow } from './catalysts';
import { PipelineModel, ROLE, type PortInformation } from './model';
import { playing, withRoles } from './roles.test.fixture';

/** Replayed by Tests/vos.BrokerContract.Tests/AgainstTheEngine/PhloemRunsADrawingThePipelinesPageSavedTests.cs. */
export const SAVED_DRAWING_FILE = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../Tests/vos.BrokerContract.Tests/AgainstTheEngine/Fixtures/the-pipelines-page-saves-a-wait-and-a-relate-box.json',
);

interface SavedRequest {
  method: 'POST' | 'PUT' | 'DELETE';
  path: string;
  body?: unknown;
}

interface SavedDrawing {
  references: string[];
  requests: SavedRequest[];
}

const DRAWING_NAME = 'Record then work out a study';

// The engine case declares a Thing of the same shape for each id the save references.
function theModel(): { model: PipelineModel; modelIds: Set<string> } {
  const things: VosThing[] = [];
  const relationships: VosRelationship[] = [];
  let n = 0;
  const T = (id: string, name: string, props: Record<string, unknown> = {}) =>
    things.push({ Id: id, Name: name, Properties: props });
  const R = (s: string, p: string, t: string) =>
    relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });
  const port = (id: string, portName: string, direction: 'in' | 'out', owner: string) => {
    T(id, id, { portName, direction, type: 'any', required: 'false' });
    R(id, 'is', 'arch-port'); R(owner, 'has', id);
  };

  T('is', 'is'); T('has', 'has'); T('carries', 'carries', playing(ROLE.WirePredicate));
  T('arch-pipeline', 'Workflow', playing(ROLE.Pipeline));
  T('arch-node', 'Step', playing(ROLE.PipelineNode));
  T('arch-input', 'Start', playing(ROLE.PipelineInput)); R('arch-input', 'is', 'arch-node');
  T('arch-output', 'Finish', playing(ROLE.PipelineOutput)); R('arch-output', 'is', 'arch-node');
  T('arch-wait', 'Pause', playing(ROLE.PipelineWait)); R('arch-wait', 'is', 'arch-node');
  T('arch-relate', 'Link up', playing(ROLE.PipelineRelate)); R('arch-relate', 'is', 'arch-node');
  T('arch-connection', 'Endpoint', playing(ROLE.Connection));
  T('arch-service', 'Capability', playing(ROLE.Service));
  T('arch-port', 'Socket', playing(ROLE.Port));
  T('arch-wire', 'Link', playing(ROLE.PipelineWire));
  T('input-connector', 'inputConnector', playing(ROLE.InputConnector));
  T('output-connector', 'outputConnector', playing(ROLE.OutputConnector));
  T('taken-on', 'takenOn', playing(ROLE.TakenOn));
  T('success', 'success', playing(ROLE.Success));
  T('fail', 'fail', playing(ROLE.Fail));
  T('stands-for', 'standsFor', playing(ROLE.StandsFor));
  T('proven-by', 'provenBy', playing(ROLE.CompletionRange));
  T('awaits-through', 'awaitsThrough', playing(ROLE.AwaitsThrough));
  T('reached-along', 'reachedAlong', playing(ROLE.ReachedAlong));
  T('notifies', 'notifies', playing(ROLE.NotifiedConnection));
  port('wait.thing.in', 'thing', 'in', 'arch-wait');
  port('wait.thing.out', 'thing', 'out', 'arch-wait');
  port('wait.reached.out', 'reached', 'out', 'arch-wait');
  port('relate.thing.in', 'thing', 'in', 'arch-relate');
  port('relate.thing.out', 'thing', 'out', 'arch-relate');
  port('relate.written.out', 'written', 'out', 'arch-relate');

  T('recorded', 'Recorded');
  T('worked-out', 'WorkedOut');
  T('works-out-service', 'works out service'); R('works-out-service', 'is', 'arch-service');
  T('works-out-connection', 'worksOut connection'); R('works-out-connection', 'is', 'arch-connection');
  R('works-out-connection', 'has', 'works-out-service'); R('works-out-connection', 'proven-by', 'worked-out');
  T('works-out', 'worksOut'); R('works-out', 'notifies', 'works-out-connection');
  const [held, related] = withRoles(things, relationships);
  return { model: new PipelineModel(held, related), modelIds: new Set(held.map((thing) => thing.Id)) };
}

const PLACEHOLDER = /^(new|created)-(\d+)$/;

function asPlaceholders(value: unknown, modelIds: Set<string>, referenced: Set<string>): unknown {
  if (typeof value === 'string') {
    const drawn = PLACEHOLDER.exec(value);
    if (drawn) return `{{${drawn[1]}:${drawn[2]}}}`;
    if (modelIds.has(value)) { referenced.add(value); return `{{model:${value}}}`; }
    return value;
  }
  if (Array.isArray(value)) return value.map((item) => asPlaceholders(item, modelIds, referenced));
  // A name is never an id, though this model's `is` and `has` have ids that read the same as their names.
  if (value && typeof value === 'object')
    return Object.fromEntries(Object.entries(value).map(([key, item]) =>
      [key, key === 'Name' ? item : asPlaceholders(item, modelIds, referenced)]));
  return value;
}

/**
 * Draws the drawing with the page's own hook and saves it, recording every request the save sends. Ids the page
 * draws read `{{new:N}}`, ids the platform answers with read `{{created:N}}`, and the model's own read `{{model:id}}`.
 */
export async function recordTheSavedDrawing(): Promise<SavedDrawing> {
  const { model, modelIds } = theModel();
  const sent: SavedRequest[] = [];
  let drawn = 0;
  let created = 0;
  const spies = [
    vi.spyOn(crypto, 'randomUUID').mockImplementation(() => `new-${++drawn}` as `${string}-${string}-${string}-${string}-${string}`),
    vi.spyOn(apiClient, 'action').mockImplementation((_description, request) => request()),
    vi.spyOn(apiClient, 'post').mockImplementation(async (path: string, body?: unknown) => {
      sent.push({ method: 'POST', path, body });
      if (path === '/api/things') return { Id: `created-${++created}`, Name: (body as { Name: string }).Name, Properties: {} };
      if (path === '/api/relationships') return { Id: `relationship-${sent.length}`, Name: '', ...(body as object), Properties: {} };
      return { thingsCreated: 0, thingsUpdated: 0, relationshipsCreated: 0, things: [] };
    }),
    vi.spyOn(apiClient, 'put').mockImplementation(async (path: string, body: unknown) => {
      sent.push({ method: 'PUT', path, body });
      return { Id: path.split('/')[3], Name: '', Properties: {} };
    }),
    vi.spyOn(apiClient, 'del').mockImplementation(async (path: string) => {
      sent.push({ method: 'DELETE', path });
      return { message: 'deleted' };
    }),
  ];
  try {
    const setError = vi.fn();
    const { result } = renderHook(() => usePipelineDrawing({ model, connections: model.connections(), setError }));
    const node = (index: number) => result.current.nodes[index];

    act(() => result.current.setName(DRAWING_NAME));
    act(() => result.current.placeStart(undefined));
    const startPort = (node(0).data as { ports: PortInformation[] }).ports[0];
    act(() => result.current.setDeclaredPorts(node(0).id, [{ ...startPort, portName: 'subject' }]));
    act(() => result.current.placeWait());
    act(() => result.current.setWait(node(1).id, { waitsFor: 'recorded', waitsAtMostMinutes: 90 }));
    act(() => result.current.placeRelate());
    act(() => result.current.setStandsFor(node(2).id, 'works-out'));
    act(() => result.current.placeOutput({ kind: 'answer' } as OutputRow));
    const endPort = (node(3).data as { ports: PortInformation[] }).ports[0];
    const wire = (from: number, sourceHandle: string, to: number, targetHandle: string) =>
      act(() => result.current.onConnect({ source: node(from).id, sourceHandle, target: node(to).id, targetHandle }));
    wire(0, startPort.portId, 1, 'wait.thing.in');
    wire(1, 'wait.thing.out', 2, 'relate.thing.in');
    wire(2, 'relate.thing.out', 3, endPort.portId);

    let saved = false;
    await act(async () => { saved = await result.current.save(); });
    if (!saved) throw new Error(`The page refused to save the drawing: ${JSON.stringify(setError.mock.calls)}`);
  } finally {
    for (const spy of spies) spy.mockRestore();
  }

  const referenced = new Set<string>();
  const requests = sent.map((request) => ({
    method: request.method,
    path: request.path.split('/').map((segment) => asPlaceholders(segment, modelIds, referenced) as string).join('/'),
    ...(request.body === undefined ? {} : { body: asPlaceholders(request.body, modelIds, referenced) }),
  }));
  return { references: [...referenced].sort(), requests };
}
