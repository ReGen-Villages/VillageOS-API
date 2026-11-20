/** How often a platform stream sends its heartbeat, whatever else it sends in between. */
const HEARTBEAT_INTERVAL_MILLISECONDS = 15000;

const SILENCE_LIMIT_MILLISECONDS = 2 * HEARTBEAT_INTERVAL_MILLISECONDS;

/** A browser passes the page events but never comments, and the heartbeat is a comment unless asked for as an event. */
export const HEARTBEAT_AS_EVENT = 'heartbeatAsEvent=true';

export interface SilenceWatch {
  heard: () => void;
  stop: () => void;
}

/** The browser calls a stream broken only when its own connection breaks, and something in between can keep
 *  that open after the platform has gone, so stopped heartbeats are the only sign. */
export function watchForSilence(source: EventSource, onSilent: () => void): SilenceWatch {
  let timer = setTimeout(onSilent, SILENCE_LIMIT_MILLISECONDS);
  const heard = () => {
    clearTimeout(timer);
    timer = setTimeout(onSilent, SILENCE_LIMIT_MILLISECONDS);
  };
  source.addEventListener('Heartbeat', heard);
  return { heard, stop: () => clearTimeout(timer) };
}
