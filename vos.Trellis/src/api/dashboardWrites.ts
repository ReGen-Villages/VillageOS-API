import type { ModelReads } from './modelReads';

/** What an endpoint answers a press with: a refusal in its own words, or what it said on taking
 *  the press — where it says anything. */
export interface EndpointAnswer {
  error?: string;
  said?: string;
}

/**
 * The post a writing widget makes: the body to the endpoint the spec names, through the same port
 * a `service` binding reads through, so a page holding no credential decides for itself what a
 * post does. Nothing here says who is asking; the platform holds the session and is what an
 * endpoint would have to be told the caller by.
 *
 * A refusal reaches the widget as the endpoint's own words whichever way the endpoint refuses —
 * with a refusing status, whose body the client already reads the error out of, or with an answer
 * carrying `error`. The client has no wording of its own to put there.
 */
export async function postToEndpoint(reads: ModelReads, via: string, body: Record<string, unknown>): Promise<EndpointAnswer> {
  try {
    return ((await reads.fromService(`/api/endpoints/${via}`, body)) as EndpointAnswer | null) ?? {};
  } catch (refusal) {
    return { error: refusal instanceof Error ? refusal.message : String(refusal) };
  }
}
