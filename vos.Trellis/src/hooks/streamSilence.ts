/** How often a platform stream sends its heartbeat, whatever else it sends in between. */
const HEARTBEAT_INTERVAL_MILLISECONDS = 15000;

const SILENCE_LIMIT_MILLISECONDS = 2 * HEARTBEAT_INTERVAL_MILLISECONDS;

/**
 * Goes on a stream's address so the platform sends its heartbeat as an event. A browser hands the
 * page an event and never a comment, which is what the heartbeat is unless asked for this way.
 */
export const HEARTBEAT_AS_EVENT = 'heartbeatAsEvent=true';

export interface SilenceWatch {
  /** Something arrived on the stream, so the wait for the next sign of life starts again. */
  heard: () => void;
  stop: () => void;
}

/**
 * Calls `onSilent` once when the stream has sent nothing for longer than two heartbeats.
 *
 * The browser reports a stream as broken only when its own connection breaks. Something between the
 * browser and the platform can keep that connection open after the platform has gone, and then the
 * only sign is that the heartbeats have stopped.
 */
export function watchForSilence(source: EventSource, onSilent: () => void): SilenceWatch {
  let timer = setTimeout(onSilent, SILENCE_LIMIT_MILLISECONDS);
  const heard = () => {
    clearTimeout(timer);
    timer = setTimeout(onSilent, SILENCE_LIMIT_MILLISECONDS);
  };
  source.addEventListener('Heartbeat', heard);
  return { heard, stop: () => clearTimeout(timer) };
}
