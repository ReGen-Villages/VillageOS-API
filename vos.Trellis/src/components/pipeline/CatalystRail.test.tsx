import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { catalystFixture } from '../../pipeline/catalysts.test.fixture';
import { catalystRail } from '../../pipeline/catalysts';
import { CatalystRail } from './CatalystRail';

function rail() {
  const { model, id } = catalystFixture();
  const onPlace = vi.fn();
  const onOpenPipeline = vi.fn();
  render(<CatalystRail groups={catalystRail(model)} onPlace={onPlace} onOpenPipeline={onOpenPipeline} />);
  return { id, onPlace, onOpenPipeline };
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
    expect(screen.getByText('starts Refill')).toBeInTheDocument();
    expect(screen.getAllByText('today: dispatched to Reader')).toHaveLength(1);
    expect(screen.getByText('today: dispatched to Handler')).toBeInTheDocument();
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
    render(<CatalystRail groups={[{ side: 'external', rows: [] }, { side: 'internal', rows: [] }]} onPlace={vi.fn()} onOpenPipeline={vi.fn()} />);
    expect(screen.getAllByText('Nothing is declared')).toHaveLength(2);
  });
});
