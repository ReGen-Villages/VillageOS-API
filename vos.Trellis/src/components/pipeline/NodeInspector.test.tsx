import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { NodeInspector } from './NodeInspector';
import type { PipelineNodeData } from './PipelineNodeView';
import type { VosThing } from '../../types/vos';

const port = (portId: string, portName: string, direction: 'in' | 'out') =>
  ({ portId, portName, direction, type: 'any', required: false });

const thing = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });
const nearest = thing('s1', 'Nearest first');
const cheapest = thing('s2', 'Cheapest first');
const site = thing('site', 'North field');
const names = new Map([nearest, cheapest, site].map((t) => [t.Id, t.Name]));

function inspect(nodeId: string, data: PipelineNodeData, handlers: Partial<Parameters<typeof NodeInspector>[0]> = {}) {
  render(<NodeInspector nodeId={nodeId} data={data} wiredInputs={new Set()}
    offeredByPort={{}} everyThing={[nearest, cheapest, site]} nameOf={(id) => names.get(id) ?? id}
    onSetPorts={vi.fn()} onSetBinding={vi.fn()} onSetChosen={vi.fn()} onSetChoices={vi.fn()} onSetRule={vi.fn()} onClose={vi.fn()} {...handlers} />);
}

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

    fireEvent.click(screen.getByLabelText('Search every Thing, not only what the ports offer'));
    fireEvent.focus(screen.getByPlaceholderText('Add a Thing for choice 1 to compare with'));

    expect(listedNames().some((name) => name.startsWith('North field'))).toBe(true);
  });

  it('adds a Thing found by searching every Thing, and turns that search off again', () => {
    const onSetChoices = vi.fn();
    const accept = { id: 'c1', name: 'Accept', when: '', comparesWith: ['s1'] };

    inspect('decide', decide({ choices: [accept] }), { ...offering, onSetChoices });
    const search = screen.getByLabelText('Search every Thing, not only what the ports offer');
    fireEvent.click(search);
    fireEvent.focus(screen.getByPlaceholderText('Add a Thing for choice 1 to compare with'));
    fireEvent.click(screen.getByText('North field'));

    expect(onSetChoices).toHaveBeenCalledWith('decide', [{ ...accept, comparesWith: ['s1', 'site'] }]);
    expect(search).not.toBeChecked();
  });

  it('shows the Things a choice compares with and stops comparing with one', () => {
    const onSetChoices = vi.fn();
    const accept = { id: 'c1', name: 'Accept', when: '', comparesWith: ['s1', 'site'] };

    inspect('decide', decide({ choices: [accept] }), { ...offering, onSetChoices });
    fireEvent.click(screen.getByLabelText('Stop comparing with North field'));

    expect(onSetChoices).toHaveBeenCalledWith('decide', [{ ...accept, comparesWith: ['s1'] }]);
  });
});
