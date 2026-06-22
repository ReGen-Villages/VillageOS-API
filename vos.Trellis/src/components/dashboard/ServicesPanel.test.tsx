import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ServicesPanel } from './ServicesPanel';
import type { RegisteredService, EndpointServiceInfo } from '../../types/mycelium';

function graphService(over: Partial<RegisteredService> = {}): RegisteredService {
  return {
    HandlerId: 'h1', ServiceName: 'consumes', EndpointUrl: 'http://localhost:7102',
    HealthStatus: 'Healthy', RegisteredAt: '', IsRunning: true, FailureCount: 0,
    Stats: { RequestsForwarded: 5, TotalResponseMilliseconds: 50, AverageResponseMilliseconds: 10 },
    IsExternal: false, ConsecutiveFailures: 0, ...over,
  };
}

function httpEndpoint(over: Partial<EndpointServiceInfo> = {}): EndpointServiceInfo {
  return {
    ThingId: 't1', Name: 'Echo', Subdomain: 'echo', ServicePort: '7110',
    Stats: { RequestCount: 3, TotalResponseMs: 30, AverageResponseMs: 10, ErrorCount: 2 }, ...over,
  };
}

const noop = () => {};

describe('ServicesPanel (unified)', () => {
  it('renders graph and http connections in one Services panel', () => {
    render(<ServicesPanel services={[graphService()]} endpoints={[httpEndpoint()]} onStart={noop} onStop={noop} />);
    expect(screen.getByText('Services')).toBeInTheDocument();
    expect(screen.getByText('consumes')).toBeInTheDocument();
    expect(screen.getByText('Echo')).toBeInTheDocument();
    // http connection shows its route; graph connection shows the predicate label
    expect(screen.getByText('/api/endpoints/echo')).toBeInTheDocument();
    expect(screen.getByText('predicate')).toBeInTheDocument();
  });

  it('shows health/running + start-stop only for graph services, errors only for endpoints', () => {
    render(<ServicesPanel services={[graphService({ IsRunning: false })]} endpoints={[httpEndpoint()]} onStart={noop} onStop={noop} />);
    expect(screen.getByText('Healthy')).toBeInTheDocument();
    expect(screen.getByText('Stopped')).toBeInTheDocument();
    expect(screen.getByTitle('Start')).toBeInTheDocument(); // control present for the stopped graph service
    expect(screen.getByText('Errors')).toBeInTheDocument(); // endpoint-only column
  });

  it('start/stop fire with the graph service handler id', () => {
    const onStart = vi.fn();
    const onStop = vi.fn();
    const { rerender } = render(<ServicesPanel services={[graphService({ IsRunning: false })]} endpoints={[]} onStart={onStart} onStop={onStop} />);
    fireEvent.click(screen.getByTitle('Start'));
    expect(onStart).toHaveBeenCalledWith('h1');

    rerender(<ServicesPanel services={[graphService({ IsRunning: true })]} endpoints={[]} onStart={onStart} onStop={onStop} />);
    fireEvent.click(screen.getByTitle('Stop'));
    expect(onStop).toHaveBeenCalledWith('h1');
  });

  it('renders the empty state when there are no connections', () => {
    render(<ServicesPanel services={[]} endpoints={[]} onStart={noop} onStop={noop} />);
    expect(screen.getByText('No services registered')).toBeInTheDocument();
  });

  it('offers delete for endpoints (with a model Thing id) and fires it; graph services have none', () => {
    const onDelete = vi.fn();
    render(<ServicesPanel services={[graphService()]} endpoints={[httpEndpoint({ ThingId: 't9', Name: 'Phloem' })]} onStart={noop} onStop={noop} onDelete={onDelete} />);
    // one delete control — only the endpoint row has a retractable Thing id
    const deleteButtons = screen.getAllByTitle('Delete (retract from model)');
    expect(deleteButtons).toHaveLength(1);
    fireEvent.click(deleteButtons[0]);
    expect(onDelete).toHaveBeenCalledWith('t9', 'Phloem');
  });

  it('omits delete controls when onDelete is not provided', () => {
    render(<ServicesPanel services={[]} endpoints={[httpEndpoint()]} onStart={noop} onStop={noop} />);
    expect(screen.queryByTitle('Delete (retract from model)')).toBeNull();
  });
});
