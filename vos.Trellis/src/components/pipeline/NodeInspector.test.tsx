import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { NodeInspector } from './NodeInspector';
import type { PipelineNodeData } from './PipelineNodeView';
import type { VosThing } from '../../types/vos';
import type { PortInformation } from '../../pipeline/model';

const port = (portId: string, portName: string, direction: 'in' | 'out') =>
  ({ portId, portName, direction, type: 'any', required: false });

const thing = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });
const nearest = thing('s1', 'Nearest first');
const cheapest = thing('s2', 'Cheapest first');
const site = thing('site', 'North field');
const names = new Map([nearest, cheapest, site].map((t) => [t.Id, t.Name]));
const SEARCH_THE_OFFERED_KINDS = 'Search every Thing of the kinds the ports offer, not only what they offer';

function inspect(nodeId: string, data: PipelineNodeData, handlers: Partial<Parameters<typeof NodeInspector>[0]> = {}) {
  render(<NodeInspector nodeId={nodeId} data={data} wiredInputs={new Set()}
    offeredByPort={{}} widerList={[nearest, cheapest, site]} onWiden={vi.fn()} nameOf={(id) => names.get(id) ?? id}
    standsForOptions={[]} onSetStandsFor={vi.fn()}
    onSetPorts={vi.fn()} onSetBinding={vi.fn()} onSetChosen={vi.fn()} onSetChoices={vi.fn()} onSetRule={vi.fn()} onClose={vi.fn()} {...handlers} />);
}

const passing = (prefix: string, portName: string) =>
  [{ ...port(`${prefix}.in`, portName, 'in'), required: true }, port(`${prefix}.out`, portName, 'out')];

const decide = (changes: Partial<PipelineNodeData> = {}): PipelineNodeData =>
  ({ label: 'Decide', ports: [port('decide.strategy', 'strategy', 'in'), port('decide.limit', 'limit', 'in')], ...changes });
const offering = { offeredByPort: { 'decide.strategy': [nearest, cheapest], 'decide.limit': [] } };
const listedNames = () => screen.queryAllByRole('button').map((button) => button.textContent ?? '');

describe('NodeInspector', () => {
  it('shows an input a wire fills as wired, found by the port identifier the wire reaches', () => {
    const data = { label: 'Merge', ports: [port('merge.in.text', 'text', 'in'), port('merge.in.count', 'count', 'in')] };

    inspect('merge', data, { wiredInputs: new Set(['merge.in.text']) });

    expect(screen.getAllByText('wired')).toHaveLength(1);
    expect(screen.getByText('text').parentElement).toHaveTextContent('wired');
    expect(screen.getByText('count').parentElement).not.toHaveTextContent('wired');
  });

  it('adds a boundary port under an identifier of its own', () => {
    const onSetPorts = vi.fn();
    const data = { label: 'Start', kind: 'input' as const, ports: [port('start.out.seed', 'seed', 'out')] };

    inspect('start', data, { onSetPorts });
    fireEvent.click(screen.getByText('+ add port'));

    const [, ports] = onSetPorts.mock.calls[0];
    expect(ports).toHaveLength(2);
    expect(ports[1].portId).toEqual(expect.any(String));
    expect(ports[1].portId).not.toBe('start.out.seed');
  });

  it('starts a new Output rule as no fail reaching the node, for the person to edit', () => {
    const onSetRule = vi.fn();

    inspect('end', { label: 'Finish', kind: 'output', ports: [] }, { onSetRule });
    expect(screen.getByText(/the run fails if a fail wire reaches this node/)).toBeInTheDocument();
    fireEvent.click(screen.getByText('+ add rule'));

    expect(onSetRule).toHaveBeenCalledWith('end', '$count(failures) = 0');
  });

  it('shows the Output rule, edits it and takes it away', () => {
    const onSetRule = vi.fn();

    inspect('end', { label: 'Finish', kind: 'output', ports: [], rule: '$count(failures) < 2' }, { onSetRule });
    const rule = screen.getByLabelText('Output rule');
    expect(rule).toHaveValue('$count(failures) < 2');
    fireEvent.change(rule, { target: { value: '$count(failures) < 3' } });
    fireEvent.click(screen.getByText('remove rule'));

    expect(onSetRule).toHaveBeenNthCalledWith(1, 'end', '$count(failures) < 3');
    expect(onSetRule).toHaveBeenNthCalledWith(2, 'end', undefined);
  });

  it.each([
    ['tell', 'Tells'],
    ['followOn', 'Starts'],
  ] as const)('shows what a %s node stands for among those it may, and chooses another', (kind, label) => {
    const onSetStandsFor = vi.fn();
    const options = [{ id: 'office', name: 'Reporting office' }, { id: 'depot', name: 'Depot' }];

    inspect('node', { label: 'Hand on', kind, ports: passing('a', 'payload'), standsForId: 'office' }, { standsForOptions: options, onSetStandsFor });
    const standsFor = screen.getByLabelText(label);
    expect(standsFor).toHaveValue('office');
    expect([...standsFor.querySelectorAll('option')].map((option) => option.textContent)).toEqual(['Reporting office', 'Depot']);
    fireEvent.change(standsFor, { target: { value: 'depot' } });

    expect(onSetStandsFor).toHaveBeenCalledWith('node', 'depot');
  });

  it('still names what a node stands for once it is no longer among those offered', () => {
    inspect('tell', { label: 'Tell', kind: 'tell', ports: [], standsForId: 'site' }, { standsForOptions: [{ id: 'office', name: 'Reporting office' }] });

    expect(screen.getByLabelText('Tells')).toHaveValue('site');
    expect(screen.getByRole('option', { name: 'North field' })).toBeInTheDocument();
  });

  it('adds, renames and removes what a tell node passes on as an in port and an out port together', () => {
    const onSetPorts = vi.fn();
    const ports = [...passing('a', 'payload'), ...passing('b', 'subject')];

    inspect('tell', { label: 'Tell', kind: 'tell', ports }, { onSetPorts });
    expect(screen.getAllByLabelText(/^Port \d name$/).map((field) => (field as HTMLInputElement).value)).toEqual(['payload', 'subject']);
    fireEvent.change(screen.getByLabelText('Port 1 name'), { target: { value: 'report' } });
    fireEvent.click(screen.getByLabelText('Remove port subject'));
    fireEvent.click(screen.getByText('+ add port'));

    const [renamed, removed, added] = onSetPorts.mock.calls.map(([, each]) => each as PortInformation[]);
    expect(renamed.filter((each) => each.portName === 'report').map((each) => each.portId)).toEqual(['a.in', 'a.out']);
    expect(removed.map((each) => each.portId)).toEqual(['a.in', 'a.out']);
    expect(added.slice(4).map((each) => [each.portName, each.direction])).toEqual([['port3', 'in'], ['port3', 'out']]);
  });

  it('binds a stop node’s in ports to run parameters, and offers it neither ports to add nor choices', () => {
    const ports = [port('things', 'things', 'in'), port('reason', 'reason', 'in'), port('requested', 'requested', 'out')];

    inspect('stop', { label: 'Stop their runs', kind: 'stop', ports });

    expect(screen.getAllByPlaceholderText('from param…')).toHaveLength(2);
    expect(screen.queryByText('+ add port')).not.toBeInTheDocument();
    expect(screen.queryByText('+ add choice')).not.toBeInTheDocument();
  });

  it('offers no rule on the start node', () => {
    inspect('start', { label: 'Start', kind: 'input', ports: [] });

    expect(screen.queryByText('+ add rule')).not.toBeInTheDocument();
  });

  it('adds a choice to a service node under an identifier of its own', () => {
    const onSetChoices = vi.fn();

    inspect('sort', { label: 'Sort', ports: [port('sort.in', 'in', 'in')] }, { onSetChoices });
    fireEvent.click(screen.getByText('+ add choice'));

    const [nodeId, choices] = onSetChoices.mock.calls[0];
    expect(nodeId).toBe('sort');
    expect(choices).toEqual([{ id: expect.any(String), name: 'choice1', when: '', comparesWith: [] }]);
  });

  it('renames a choice, edits its rule and removes it', () => {
    const onSetChoices = vi.fn();
    const large = { id: 'c1', name: 'Large', when: 'outputs.size > 10', comparesWith: [] };

    inspect('sort', { label: 'Sort', ports: [], choices: [large] }, { onSetChoices });
    fireEvent.change(screen.getByLabelText('Name of choice 1'), { target: { value: 'Big' } });
    fireEvent.change(screen.getByLabelText('Rule of choice 1'), { target: { value: 'outputs.size > 20' } });
    fireEvent.click(screen.getByLabelText('Remove choice Large'));

    expect(onSetChoices.mock.calls.map(([, choices]) => choices)).toEqual([
      [{ ...large, name: 'Big' }],
      [{ ...large, when: 'outputs.size > 20' }],
      [],
    ]);
  });

  it('shows what the node chose for an in port that offers Things, and changes or clears it', () => {
    const onSetChosen = vi.fn();

    inspect('decide', decide({ chosen: { 'decide.strategy': 's2' } }), { ...offering, onSetChosen });
    const strategy = screen.getByLabelText('Chosen for strategy');
    expect(strategy).toHaveValue('s2');
    expect(screen.queryByLabelText('Chosen for limit')).not.toBeInTheDocument();
    fireEvent.change(strategy, { target: { value: 's1' } });
    fireEvent.change(strategy, { target: { value: '' } });

    expect(onSetChosen.mock.calls).toEqual([['decide', { 'decide.strategy': 's1' }], ['decide', {}]]);
  });

  it('lists only the offered Things for a choice to compare with until every Thing is searched', () => {
    inspect('decide', decide({ choices: [{ id: 'c1', name: 'Accept', when: '', comparesWith: [] }] }), offering);
    fireEvent.focus(screen.getByPlaceholderText('Add a Thing for choice 1 to compare with'));
    expect(listedNames().some((name) => name.startsWith('North field'))).toBe(false);
    expect(listedNames().some((name) => name.startsWith('Nearest first'))).toBe(true);

    fireEvent.click(screen.getByLabelText(SEARCH_THE_OFFERED_KINDS));
    fireEvent.focus(screen.getByPlaceholderText('Add a Thing for choice 1 to compare with'));

    expect(listedNames().some((name) => name.startsWith('North field'))).toBe(true);
  });

  it('adds a Thing found by searching the offered kinds, and turns that search off again', () => {
    const onSetChoices = vi.fn();
    const accept = { id: 'c1', name: 'Accept', when: '', comparesWith: ['s1'] };

    inspect('decide', decide({ choices: [accept] }), { ...offering, onSetChoices });
    const search = screen.getByLabelText(SEARCH_THE_OFFERED_KINDS);
    fireEvent.click(search);
    fireEvent.focus(screen.getByPlaceholderText('Add a Thing for choice 1 to compare with'));
    fireEvent.click(screen.getByText('North field'));

    expect(onSetChoices).toHaveBeenCalledWith('decide', [{ ...accept, comparesWith: ['s1', 'site'] }]);
    expect(search).not.toBeChecked();
  });

  // The page fetches the wider list only while a choice is searching it, so it has to be told when one
  // starts and when the last one stops — a search turned off by an addition, or by the inspector closing.
  it('tells the page while any choice searches the offered kinds, and says how many Things they hold', () => {
    const onWiden = vi.fn();
    const choices = [
      { id: 'c1', name: 'Accept', when: '', comparesWith: [] },
      { id: 'c2', name: 'Refuse', when: '', comparesWith: [] },
    ];

    const { unmount } = render(<NodeInspector nodeId="decide" data={decide({ choices })} wiredInputs={new Set()}
      offeredByPort={offering.offeredByPort} widerList={[nearest, cheapest, site]} onWiden={onWiden} nameOf={(id) => names.get(id) ?? id}
      standsForOptions={[]} onSetStandsFor={vi.fn()} onSetPorts={vi.fn()} onSetBinding={vi.fn()} onSetChosen={vi.fn()} onSetChoices={vi.fn()} onSetRule={vi.fn()} onClose={vi.fn()} />);
    expect(onWiden).toHaveBeenLastCalledWith(false);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();

    const [first, second] = screen.getAllByLabelText(SEARCH_THE_OFFERED_KINDS);
    fireEvent.click(first);
    expect(onWiden).toHaveBeenLastCalledWith(true);
    expect(screen.getByRole('status')).toHaveTextContent('3 Things of those kinds');

    fireEvent.click(second);
    fireEvent.click(first);
    expect(onWiden).toHaveBeenLastCalledWith(true);

    fireEvent.click(second);
    expect(onWiden).toHaveBeenLastCalledWith(false);

    fireEvent.click(first);
    expect(onWiden).toHaveBeenLastCalledWith(true);
    unmount();
    expect(onWiden).toHaveBeenLastCalledWith(false);
  });

  it('shows the Things a choice compares with and stops comparing with one', () => {
    const onSetChoices = vi.fn();
    const accept = { id: 'c1', name: 'Accept', when: '', comparesWith: ['s1', 'site'] };

    inspect('decide', decide({ choices: [accept] }), { ...offering, onSetChoices });
    fireEvent.click(screen.getByLabelText('Stop comparing with North field'));

    expect(onSetChoices).toHaveBeenCalledWith('decide', [{ ...accept, comparesWith: ['s1'] }]);
  });
});
