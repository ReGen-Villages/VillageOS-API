/** `connecting` is no stream yet, or one opened on purpose; `lost` is a failed stream being retried. They are
 *  told apart because only the second is a fault, and drawn alike, every sign-in would report one. */
export const CONNECTION_STATES = ['connecting', 'live', 'lost'] as const;

export type ConnectionState = (typeof CONNECTION_STATES)[number];
