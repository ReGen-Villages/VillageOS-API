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
  it('offers the answer, the other pipelines, the systems told through a connection with what each is told, and stopping runs', () => {
    rail();
    expect(screen.getByText('The answer')).toBeInTheDocument();
    expect(screen.getByText('Refill')).toBeInTheDocument();
    expect(screen.queryByText('Readings arrive')).toBeNull();
    expect(screen.getByText('told daily digest')).toBeInTheDocument();
    expect(screen.queryByText('Weather station')).toBeNull();
    expect(screen.getByText('Stop their runs')).toBeInTheDocument();
  });

  it.each([
    ['Place an end node standing for The answer', 'answer'],
    ['Place a follow-on node starting Refill', 'pipeline'],
    ['Place a tell node telling Reporting office', 'externalSystem'],
    ['Place a stop node', 'stop'],
  ])('says what pressing "%s" places, and places it', (name, kind) => {
    const { onPlaceOutput } = rail();
    fireEvent.click(screen.getByRole('button', { name }));
    expect(onPlaceOutput).toHaveBeenCalledWith(expect.objectContaining({ kind }));
  });

  it('says a system told through a connection is told nothing yet, and offers no stopping where the model binds no stop kind', () => {
    const { model } = catalystFixture({ bindsTellFollowOnAndStop: false });
    const silent = { id: 'office', kind: 'externalSystem' as const, name: 'Quiet office', told: [] };
    render(<OutputRail outputs={[...outputRail(model, null), silent]} connections={[]} onPlaceOutput={vi.fn()} onAddService={vi.fn()} />);

    expect(screen.getByText('told nothing yet')).toBeInTheDocument();
    expect(screen.queryByText('Stopping runs')).toBeNull();
  });

  it('lists every service a node may call under the outputs', () => {
    const { id, onAddService } = rail();
    const services = screen.getByRole('list', { name: 'Services' });
    fireEvent.click(within(services).getByRole('button', { name: /intake/ }));
    expect(onAddService).toHaveBeenCalledWith(expect.objectContaining({ connectionId: id.intakeConnection, subdomain: 'intake' }));
  });
});
