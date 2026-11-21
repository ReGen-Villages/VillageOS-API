import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { catalystFixture } from '../../pipeline/catalysts.test.fixture';
import { catalystRail, type CatalystGroup } from '../../pipeline/catalysts';
import { CatalystRail } from './CatalystRail';

function rail() {
  const { model, id } = catalystFixture();
  const onPlace = vi.fn();
  const onOpenPipeline = vi.fn();
  const onHandOver = vi.fn();
  const onShowSettings = vi.fn();
  render(<CatalystRail groups={catalystRail(model)} onPlace={onPlace} onOpenPipeline={onOpenPipeline} onHandOver={onHandOver} onShowSettings={onShowSettings} />);
  return { id, onPlace, onOpenPipeline, onHandOver, onShowSettings };
}

const EMPTY: CatalystGroup[] = (['messages', 'states', 'clock', 'relationships'] as const).map((name) => ({ name, rows: [] }));

describe('CatalystRail', () => {
  it('lists each catalyst under its group', () => {
    rail();
    const group = (name: string) => within(screen.getByRole('region', { name }));
    expect(group('Messages from outside').getByText('Weather station · hourly reading')).toBeInTheDocument();
    expect(group('Messages from outside').getByText('anybody · spare')).toBeInTheDocument();
    expect(group('States the model enters').getByText('Reservoir · running low')).toBeInTheDocument();
    expect(group('States the model enters').getByText('Reservoir · overdue · every 60 s')).toBeInTheDocument();
    expect(group('The clock').getByText('every hour · every 60 model min')).toBeInTheDocument();
    expect(group('Relationships written').getByText('feeds')).toBeInTheDocument();
    expect(group('Relationships written').getByText('overflows')).toBeInTheDocument();
  });

  it('names the drawing that handles a catalyst in its handling cell', () => {
    rail();
    expect(screen.getByTitle('starts Readings arrive')).toHaveTextContent('Readings arrive');
    expect(screen.getByTitle('starts Tally')).toHaveTextContent('Tally');
  });

  it('names the service of a state drawn from but sent elsewhere, and keeps the drawing as the row\'s link', () => {
    const { id, onOpenPipeline } = rail();
    expect(screen.queryByTitle('starts Refill')).not.toBeInTheDocument();
    expect(screen.getAllByTitle('today: sent to Watcher').map((cell) => cell.textContent)).toEqual(['Watcher', 'Watcher']);
    fireEvent.click(screen.getByRole('button', { name: 'Reservoir · running low' }));
    expect(onOpenPipeline).toHaveBeenCalledWith(id.refill);
  });

  it('says nothing handles a state sent to no service, or one the orchestrator refuses since nothing is drawn', () => {
    rail();
    expect(screen.getByTitle('nothing happens today')).toHaveTextContent('nothing');
    expect(screen.getByTitle('the orchestrator refuses each entry: nothing is drawn')).toHaveTextContent('nothing');
  });

  it('says nothing handles a connection used as a predicate that names no connection along notifies, and why', () => {
    rail();
    expect(screen.getByTitle('the platform calls nothing for it: it names no connection along alerts')).toHaveTextContent('nothing');
  });

  it('offers the orchestrator on each state it does not handle yet, and hands that row over', () => {
    const { id, onHandOver } = rail();
    expect(screen.getAllByRole('button', { name: /with the orchestrator$/ })).toHaveLength(3);
    fireEvent.click(screen.getByRole('button', { name: 'Handle Reservoir · running low with the orchestrator' }));
    expect(onHandOver).toHaveBeenCalledWith(expect.objectContaining({ standsForId: id.runningLow }));
  });

  it('opens the pipeline a catalyst starts when its words are pressed', () => {
    const { id, onOpenPipeline } = rail();
    fireEvent.click(screen.getByRole('button', { name: 'Weather station · hourly reading' }));
    expect(onOpenPipeline).toHaveBeenCalledWith(id.readingsArrive);
  });

  it('places a start node standing for the row, and one standing for nothing from the start by hand', () => {
    const { id, onPlace } = rail();
    fireEvent.click(screen.getByRole('button', { name: 'Place a start node standing for every hour · every 60 model min' }));
    expect(onPlace).toHaveBeenCalledWith(expect.objectContaining({ kind: 'cadence', standsForId: id.everyHour }));
    fireEvent.click(screen.getByRole('button', { name: 'Place a start by hand' }));
    expect(onPlace).toHaveBeenLastCalledWith(undefined);
  });

  it('offers no start node for a catalyst no run can start from', () => {
    rail();
    expect(screen.queryByRole('button', { name: 'Place a start node standing for feeds' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Place a start node standing for overflows' })).toBeInTheDocument();
  });

  it('says a group holds nothing rather than drawing an empty list, and shows every group', () => {
    render(<CatalystRail groups={EMPTY} onPlace={vi.fn()} onOpenPipeline={vi.fn()} onHandOver={vi.fn()} onShowSettings={vi.fn()} />);
    expect(screen.getAllByRole('region')).toHaveLength(4);
    expect(screen.getAllByText('Nothing is declared')).toHaveLength(4);
  });

  it('offers the call settings of a state watched through one connection, and nothing else', () => {
    const { id, onShowSettings } = rail();
    expect(screen.getAllByRole('button', { name: /^Show the call settings of / }).map((button) => button.getAttribute('aria-label'))).toEqual([
      'Show the call settings of Reservoir · drought',
      'Show the call settings of Reservoir · dry',
      'Show the call settings of Reservoir · overdue · every 60 s',
      'Show the call settings of Reservoir · running low',
      'Show the call settings of Reservoir · surplus',
    ]);
    fireEvent.click(screen.getByRole('button', { name: 'Show the call settings of Reservoir · running low' }));
    expect(onShowSettings).toHaveBeenCalledWith(id.watchesRunningLow);
  });
});
