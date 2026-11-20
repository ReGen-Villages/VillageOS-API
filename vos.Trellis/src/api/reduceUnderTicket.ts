import type { TemporalReduceQuery, TemporalReduceResponse } from '@tendril-ui/tendril';
import { findingsApi } from './findingsApi';

/** The service scopes the question to the submission's own site, so the Thing the resolver named is dropped. */
export async function reduceUnderTicket(
  query: TemporalReduceQuery,
  submissionId: string,
  ticket: { current: string | null },
  noTicket: string,
): Promise<TemporalReduceResponse> {
  const held = ticket.current;
  if (held === null) throw new Error(noTicket);
  const { thingId: _scopedByTheService, ...question } = query;
  const reduced = await findingsApi.reduceWithTicket(submissionId, held, question);
  ticket.current = reduced.ticket;
  return reduced.answer;
}
