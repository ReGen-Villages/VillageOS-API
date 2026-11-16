import type { ModelReads } from './modelReads';
import { tendrilWord } from '../i18n/words';

/** What an endpoint answers a press with: a refusal in its own words, or what it said on taking
 *  the press — where it says anything. */
export interface EndpointAnswer {
  error?: string;
  said?: string;
}

/**
 * The post a writing widget makes: the body to the route the specification names, through the
 * application's `press`, which says who is pressing where the application knows. A page that supplies
 * no `press` cannot press, and is refused in Tendril's words without anything being sent.
 *
 * A refusal reaches the widget whichever way the endpoint refuses — with a refusing status, whose
 * body the client already reads the error out of, or with an answer carrying `error` — in the
 * application's words where it gives some for that answer, and otherwise in the endpoint's own.
 */
export async function postToEndpoint(
  reads: ModelReads,
  via: string,
  body: Record<string, unknown>,
  wordRefusal?: (answer: Record<string, unknown>) => string | undefined,
): Promise<EndpointAnswer> {
  if (!reads.press) return { error: tendrilWord('cannotPress') };
  let answer: Record<string, unknown>;
  try {
    answer = ((await reads.press(routeOf(via), body)) as Record<string, unknown> | null) ?? {};
  } catch (refusal) {
    answer = { error: refusal instanceof Error ? refusal.message : String(refusal) };
  }
  // Where it went, never what was in it: a form's body is what a person typed about themselves.
  reads.recordAction?.(`send a form to ${via}`, answer.error === undefined);
  if (answer.error === undefined) return answer;
  return { ...answer, error: wordRefusal?.(answer) ?? String(answer.error) };
}

/** Where a `via` posts: a route on the platform itself where the specification wrote one by its path, and
 *  otherwise the endpoint the forwarder resolves by that name. */
function routeOf(via: string): string {
  return isPlatformRoute(via) ? via : `/api/endpoints/${via}`;
}

export function isPlatformRoute(via: string): boolean {
  return via.startsWith('/');
}
