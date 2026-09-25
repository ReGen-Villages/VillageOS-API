import { createContext } from 'react';
import type { RequestParameters } from 'maplibre-gl';

type Headers = () => Record<string, string>;

/** What a map adds to the requests it makes of the server that served its page. The signed-in app
 *  provides its sign-in, because the broker serves a model's tiles only to someone who may read it. The
 *  public form provides nothing: the map is shared with it, and it must not reach sign-in code. */
export const OwnServerRequestHeaders = createContext<Headers | null>(null);

/** maplibre's request hook. Only the page's own server is given the headers, so a token never travels
 *  to a map provider. */
export function carryingOwnServerHeaders(pageOrigin: string, headers: Headers | null) {
  return (url: string): RequestParameters | undefined =>
    headers && new URL(url, pageOrigin).origin === pageOrigin ? { url, headers: headers() } : undefined;
}
