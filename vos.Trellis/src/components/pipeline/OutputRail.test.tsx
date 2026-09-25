import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { catalystFixture } from '../../pipeline/catalysts.test.fixture';
import { outputRail } from '../../pipeline/catalysts';
import { OutputRail } from './OutputRail';

function rail() {
  const { model, id } = catalystFixture();
  const onPlaceOutput = vi.fn();
  const onAddService = vi.fn();
  render(<OutputRail outputs={outputRail(model, id.readingsArrive)} connections={model.connections()} onPlaceOutput={onPlaceOutput} onAddService={onAddService} />);
  return { id, onPlaceOutput, onAddService };
}

describe('OutputRail', () => {
  it('offers the answer, the other pipelines and the external systems with what each is told', () => {
    rail();
    expect(screen.getByText('The answer')).toBeInTheDocument();
    expect(screen.getByText('Refill')).toBeInTheDocument();
    expect(screen.queryByText('Readings arrive')).toBeNull();
    expect(screen.getByText('told daily digest')).toBeInTheDocument();
    expect(screen.getByText('told nothing yet')).toBeInTheDocument();
  });

  it('places an end node standing for what was pressed', () => {
    const { id, onPlaceOutput } = rail();
    fireEvent.click(screen.getByRole('button', { name: 'Place an end node standing for Reporting office' }));
    expect(onPlaceOutput).toHaveBeenCalledWith(expect.objectContaining({ kind: 'externalSystem', id: id.reportingOffice }));
    fireEvent.click(screen.getByRole('button', { name: 'Place an end node standing for The answer' }));
    expect(onPlaceOutput).toHaveBeenLastCalledWith(expect.objectContaining({ kind: 'answer' }));
  });

  it('lists every service a node may dispatch under the outputs', () => {
    const { id, onAddService } = rail();
    const services = screen.getByRole('list', { name: 'Services' });
    fireEvent.click(within(services).getByRole('button', { name: /intake/ }));
    expect(onAddService).toHaveBeenCalledWith(expect.objectContaining({ connectionId: id.intakeDoor, subdomain: 'intake' }));
  });
});
