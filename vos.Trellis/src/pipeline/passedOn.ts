import type { PortInformation } from './model';

// A tell or follow-on node passes on each value it takes in, through an in port and an out port of one name, so the
// two are added, renamed and removed together.

export function passedOn(portName: string): PortInformation[] {
  return [
    { portId: crypto.randomUUID(), portName, direction: 'in', type: 'any', required: true },
    { portId: crypto.randomUUID(), portName, direction: 'out', type: 'any', required: false },
  ];
}

// Two values sharing a name, as they do while one is typed over the other, are paired by their position in the list.
function passedOnThrough(ports: PortInformation[], taken: PortInformation): PortInformation | undefined {
  const named = (direction: 'in' | 'out') => ports.filter((port) => port.direction === direction && port.portName === taken.portName);
  return named('out')[named('in').indexOf(taken)];
}

function withPartner(ports: PortInformation[], inPortId: string): Set<string> {
  const taken = ports.find((port) => port.portId === inPortId && port.direction === 'in');
  return new Set(taken ? [taken.portId, passedOnThrough(ports, taken)?.portId].filter((id): id is string => id !== undefined) : []);
}

export function renamePassedOn(ports: PortInformation[], inPortId: string, portName: string): PortInformation[] {
  const renamed = withPartner(ports, inPortId);
  return ports.map((port) => (renamed.has(port.portId) ? { ...port, portName } : port));
}

export function removePassedOn(ports: PortInformation[], inPortId: string): PortInformation[] {
  const removed = withPartner(ports, inPortId);
  return ports.filter((port) => !removed.has(port.portId));
}
