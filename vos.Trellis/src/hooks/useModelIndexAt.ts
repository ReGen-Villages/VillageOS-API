import { useEffect, useState } from 'react';
import { modelApi } from '../api/modelApi';
import { modelIndexFor, type ModelIndex } from '@tendril-ui/tendril';
import type { InheritedPropertySet, VosRelationship, VosThing } from '@tendril-ui/tendril';

/** How long a chosen instant is left alone before it is read for. Long enough that spinning a
 *  datetime field through its segments asks once rather than once a segment. */
const MOMENT_SETTLES_FOR_MILLISECONDS = 400;

/** Nothing until the read arrives, because the rows standing now are not the moment asked for, and no
 *  states, because those are a live reading. Whether a Thing is a kind comes from the live index, because
 *  the read does not carry it, the index needs it, and it cannot change after creation. */
export function useModelIndexAt(instant: string | undefined, live: ModelIndex): { index: ModelIndex | null; failed: boolean } {
  const [read, setRead] = useState<{ instant: string; things: VosThing[]; relationships: VosRelationship[] } | null>(null);
  const [refused, setRefused] = useState<string | null>(null);

  useEffect(() => {
    if (!instant) return;
    // The platform answers an instant only with the whole model, so the instant is left to settle first,
    // and a read that the next one replaces is cancelled.
    const asking = new AbortController();
    const settling = setTimeout(() => {
      modelApi.getAtTime(instant, asking.signal)
        .then((snapshot) => setRead({
          instant,
          things: snapshot.Things.map((thing) => ({
            Id: thing.Id,
            Name: thing.Name,
            Properties: thing.Properties,
            InheritedOverrides: thing.InheritedOverrides as unknown as Record<string, InheritedPropertySet>,
            IsArchetype: live.archetypeIds.has(thing.Id) || undefined,
          })),
          relationships: snapshot.Relationships.map((edge) => ({
            Id: edge.Id,
            SubjectId: edge.SubjectId,
            PredicateId: edge.PredicateId,
            TargetId: edge.TargetId,
            Properties: edge.Properties,
          })),
        }))
        .catch(() => { if (!asking.signal.aborted) setRefused(instant); });
    }, MOMENT_SETTLES_FOR_MILLISECONDS);
    return () => { clearTimeout(settling); asking.abort(); };
  }, [instant, live]);

  // Both answers are held under the instant they were given for, so a moment just chosen carries
  // neither the rows nor the refusal of the one before it.
  const stood = read?.instant === instant ? read : null;
  return {
    index: stood ? modelIndexFor(stood.things, stood.relationships) : null,
    failed: refused === instant,
  };
}
