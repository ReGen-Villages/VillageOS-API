import { describe, it, expect } from 'vitest';
import { passedOn, renamePassedOn, removePassedOn } from './passedOn';
import type { PortInformation } from './model';

const port = (portId: string, portName: string, direction: 'in' | 'out'): PortInformation =>
  ({ portId, portName, direction, type: 'any', required: direction === 'in' });

describe('the values a tell or follow-on node passes on', () => {
  it('passes a value on through a required in port and an out port of its name, each under an identifier of its own', () => {
    const [taken, given] = passedOn('payload');

    expect(taken).toMatchObject({ portName: 'payload', direction: 'in', required: true });
    expect(given).toMatchObject({ portName: 'payload', direction: 'out', required: false });
    expect(taken.portId).not.toBe(given.portId);
  });

  it('renames the in port and the out port it passes on through, keeping both identifiers', () => {
    const ports = [port('a.in', 'payload', 'in'), port('a.out', 'payload', 'out'), port('b.in', 'subject', 'in'), port('b.out', 'subject', 'out')];

    expect(renamePassedOn(ports, 'a.in', 'report')).toEqual([
      port('a.in', 'report', 'in'), port('a.out', 'report', 'out'), port('b.in', 'subject', 'in'), port('b.out', 'subject', 'out'),
    ]);
  });

  it('keeps two values apart while they share a name, as they do while one is typed over the other', () => {
    const sharing = [port('a.in', 'payload', 'in'), port('a.out', 'payload', 'out'), port('b.in', 'payload', 'in'), port('b.out', 'payload', 'out')];

    expect(renamePassedOn(sharing, 'b.in', 'payload2')).toEqual([
      port('a.in', 'payload', 'in'), port('a.out', 'payload', 'out'), port('b.in', 'payload2', 'in'), port('b.out', 'payload2', 'out'),
    ]);
    expect(removePassedOn(sharing, 'b.in').map((each) => each.portId)).toEqual(['a.in', 'a.out']);
  });

  it('removes the in port and the out port it passes on through', () => {
    const ports = [port('a.in', 'payload', 'in'), port('b.in', 'subject', 'in'), port('a.out', 'payload', 'out'), port('b.out', 'subject', 'out')];

    expect(removePassedOn(ports, 'a.in').map((each) => each.portId)).toEqual(['b.in', 'b.out']);
  });
});
