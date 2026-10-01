/**
 * Whether changes are reaching the page.
 *
 * `connecting` is no stream yet, or one being opened on purpose: the first open, or a page changing
 * what its subscription covers. `lost` is a stream that failed and is being retried. They are told
 * apart because only the second is a fault, and drawn alike every sign-in reports one.
 */
export const CONNECTION_STATES = ['connecting', 'live', 'lost'] as const;

export type ConnectionState = (typeof CONNECTION_STATES)[number];
