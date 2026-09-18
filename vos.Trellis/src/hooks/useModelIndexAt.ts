import { useEffect, useState } from 'react';
import { modelApi } from '../api/modelApi';
import { modelIndexFor, type ModelIndex } from '../api/dashboardApi';
import type { InheritedPropertySet, VosRelationship, VosThing } from '../types/vos';

/** How long a chosen instant is left alone before it is read for. Long enough that spinning a
 *  datetime field through its segments asks once rather than once a segment. */
const MOMENT_SETTLES_FOR_MILLISECONDS = 400;

/** The model as it stood at an instant, indexed the way the live model is — and nothing at all
 *  until that read lands, because the rows standing now are not the ones a moment was asked for.
 *  A moment carries no state: the rows a state holds are a live reading.
 *
 *  The read answers each Thing's own properties and the overrides it held, but not whether it
 *  declared itself a kind; the index needs that to tell a sub-kind from an instance, and the
 *  declaration cannot change after creation, so it is taken from the live index. */
export function useModelIndexAt(instant: string | undefined, live: ModelIndex): { index: ModelIndex | null; failed: boolean } {
  const [read, setRead] = useState<{ instant: string; things: VosThing[]; relationships: VosRelationship[] } | null>(null);
  const [refused, setRefused] = useState<string | null>(null);

  useEffect(() => {
    if (!instant) return;
    // The platform answers a moment with the whole model and nothing narrower — no timestamp on the
    // read that takes a type and a property list — so one read is the static model at least, and a
    // field spun through its minutes would ask for several. The instant is left to settle, and a
    // read the next one supersedes is abandoned rather than left running.
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
