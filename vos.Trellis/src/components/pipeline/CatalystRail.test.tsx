import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { catalystFixture } from '../../pipeline/catalysts.test.fixture';
import { catalystRail, type CatalystRow } from '../../pipeline/catalysts';
import { CatalystRail } from './CatalystRail';

function rail() {
  const { model, id } = catalystFixture();
  const onPlace = vi.fn();
  const onOpenPipeline = vi.fn();
  const onHandOver = vi.fn();
  render(<CatalystRail groups={catalystRail(model)} onPlace={onPlace} onOpenPipeline={onOpenPipeline} onHandOver={onHandOver} />);
  return { id, onPlace, onOpenPipeline, onHandOver };
}

describe('CatalystRail', () => {
  it('lists what arrives from outside apart from what the model does on its own', () => {
    rail();
    const external = screen.getByRole('region', { name: 'From outside' });
    const internal = screen.getByRole('region', { name: 'From the model' });
    expect(within(external).getByText('Weather station · hourly reading')).toBeInTheDocument();
    expect(within(external).getByText('anybody · spare')).toBeInTheDocument();
    expect(within(internal).getByText('Reservoir · below reorder')).toBeInTheDocument();
    expect(within(internal).getByText('Reservoir · overdue · every 60 s')).toBeInTheDocument();
    expect(within(internal).getByText('feeds')).toBeInTheDocument();
  });

  it('says what each catalyst starts, or what happens to it today', () => {
    rail();
    expect(screen.getByText('starts Readings arrive')).toBeInTheDocument();
    expect(screen.getByText('starts Store surplus')).toBeInTheDocument();
    expect(screen.getAllByText('today: dispatched to Reader')).toHaveLength(1);
    expect(screen.getByText('today: dispatched to Handler')).toBeInTheDocument();
  });

  it('says of a state drawn from but sent elsewhere both the drawing and the service that receives it', () => {
    rail();
    expect(screen.queryByText('starts Refill')).not.toBeInTheDocument();
    expect(screen.getByText('drawn: Refill · today: dispatched to Watcher')).toBeInTheDocument();
  });

  it('says a state the orchestrator handles with nothing drawn is refused each time', () => {
    rail();
    expect(screen.getByText('the orchestrator refuses each entry: nothing is drawn')).toBeInTheDocument();
  });

  it('says of a state drawn from but sent to no service that nothing happens to it today', () => {
    const row: CatalystRow = {
      id: 'state:dry', kind: 'state', who: 'Reservoir', what: 'dry', standsForId: 'dry', today: '',
      starts: { id: 'p', name: 'Refill' }, handling: { connectionId: 'c', byOrchestrator: false }, offersOrchestrator: true,
    };
    render(<CatalystRail groups={[{ side: 'external', rows: [] }, { side: 'internal', rows: [row] }]} onPlace={vi.fn()} onOpenPipeline={vi.fn()} onHandOver={vi.fn()} />);
    expect(screen.getByText('drawn: Refill · nothing happens today')).toBeInTheDocument();
  });

  it('offers the orchestrator on each state it does not handle yet, and hands that row over', () => {
    const { id, onHandOver } = rail();
    expect(screen.getAllByRole('button', { name: /with the orchestrator$/ })).toHaveLength(3);
    fireEvent.click(screen.getByRole('button', { name: 'Handle Reservoir · below reorder with the orchestrator' }));
    expect(onHandOver).toHaveBeenCalledWith(expect.objectContaining({ standsForId: id.belowReorder }));
  });

  it('opens the pipeline a catalyst starts when its words are pressed', () => {
    const { id, onOpenPipeline } = rail();
    fireEvent.click(screen.getByRole('button', { name: 'Weather station · hourly reading' }));
    expect(onOpenPipeline).toHaveBeenCalledWith(id.readingsArrive);
  });

  it('places a start node standing for the row, and one standing for nothing from the start by hand', () => {
    const { id, onPlace } = rail();
    fireEvent.click(screen.getByRole('button', { name: 'Place a start node standing for Reservoir · below reorder' }));
    expect(onPlace).toHaveBeenCalledWith(expect.objectContaining({ kind: 'state', standsForId: id.belowReorder }));
    fireEvent.click(screen.getByRole('button', { name: 'Place a start by hand' }));
    expect(onPlace).toHaveBeenLastCalledWith(undefined);
  });

  it('says the kind once for each run of rows of that kind', () => {
    rail();
    const internal = screen.getByRole('region', { name: 'From the model' });
    expect(within(internal).getAllByText('a state is entered')).toHaveLength(1);
    expect(within(screen.getByRole('region', { name: 'From outside' })).getAllByText('a message arrives')).toHaveLength(1);
  });

  it('says a side holds nothing rather than drawing an empty list', () => {
    render(<CatalystRail groups={[{ side: 'external', rows: [] }, { side: 'internal', rows: [] }]} onPlace={vi.fn()} onOpenPipeline={vi.fn()} onHandOver={vi.fn()} />);
    expect(screen.getAllByText('Nothing is declared')).toHaveLength(2);
  });
});
