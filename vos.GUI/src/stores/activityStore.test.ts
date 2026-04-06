import { describe, it, expect, beforeEach } from 'vitest';
import { useActivityStore } from './activityStore';
import type { ActivityEvent } from '../types/broker';

function makeEvent(type: string, desc: string): ActivityEvent {
  return { Type: type, Timestamp: new Date().toISOString(), Description: desc };
}

describe('activityStore', () => {
  beforeEach(() => {
    useActivityStore.setState({ events: [] });
  });

  it('starts with an empty event list', () => {
    expect(useActivityStore.getState().events).toEqual([]);
  });

  it('pushEvent appends an event', () => {
    useActivityStore.getState().pushEvent(makeEvent('ThingCreated', 'Created Alice'));
    const events = useActivityStore.getState().events;
    expect(events).toHaveLength(1);
    expect(events[0].Description).toBe('Created Alice');
  });

  it('pushEvent appends multiple events in order', () => {
    const { pushEvent } = useActivityStore.getState();
    pushEvent(makeEvent('ThingCreated', 'first'));
    pushEvent(makeEvent('ThingDeleted', 'second'));
    pushEvent(makeEvent('PropertyChanged', 'third'));

    const events = useActivityStore.getState().events;
    expect(events).toHaveLength(3);
    expect(events.map((e) => e.Description)).toEqual(['first', 'second', 'third']);
  });

  it('caps events at 200', () => {
    const { pushEvent } = useActivityStore.getState();
    for (let i = 0; i < 210; i++) {
      pushEvent(makeEvent('ThingCreated', `event-${i}`));
    }

    const events = useActivityStore.getState().events;
    expect(events).toHaveLength(200);
    // Oldest events should be trimmed — first event should be event-10
    expect(events[0].Description).toBe('event-10');
    expect(events[199].Description).toBe('event-209');
  });

  it('clear resets events to empty', () => {
    const state = useActivityStore.getState();
    state.pushEvent(makeEvent('ThingCreated', 'will be cleared'));
    state.clear();
    expect(useActivityStore.getState().events).toEqual([]);
  });
});
