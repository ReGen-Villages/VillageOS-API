import { useEffect, useRef } from 'react';
import { useModelStore } from '../stores/modelStore';
import type { CoveredObjects } from '../types/subscription';
import type { VosRelationship } from '../types/vos';
import { SUBSCRIPTION_OPENED, addToSubscription, removeFromSubscription, useSse } from './useSse';

/** What one widening brought to the page that it did not already hold, to take away again. */
interface BroughtIn {
  things: Set<string>;
  relationships: VosRelationship[];
}

const NOTHING: BroughtIn = { things: new Set(), relationships: [] };

function broughtIn(covered: CoveredObjects): BroughtIn {
  const { things, relationships } = useModelStore.getState();
  const heldThings = new Set(things.map((thing) => thing.Id));
  const heldRelationships = new Set(relationships.map((relationship) => relationship.Id));
  return {
    things: new Set(covered.things.map((thing) => thing.Id).filter((id) => !heldThings.has(id))),
    relationships: covered.relationships.filter((relationship) => !heldRelationships.has(relationship.Id)),
  };
}

/** The Things of the kinds, except those the page still refers to, with every relationship brought in that
 *  touches one; a relationship between two Things the page keeps is kept with them. */
function toTakeAway(brought: BroughtIn, stillReferenced: Set<string>): { things: string[]; relationships: string[] } {
  const things = [...brought.things].filter((id) => !stillReferenced.has(id));
  const leaving = new Set(things);
  const relationships = brought.relationships
    .filter((relationship) => leaving.has(relationship.SubjectId) || leaving.has(relationship.TargetId))
    .map((relationship) => relationship.Id);
  return { things, relationships };
}

function putInStore(covered: CoveredObjects): void {
  useModelStore.getState().applyBatch({
    thingUpserts: covered.things,
    relationshipUpserts: covered.relationships,
    thingStateUpdates: [...covered.thingStates].map(([id, states]) => ({ id, states })),
  });
}

/**
 * Keep every Thing of `kindIds` in the subscription and the store while the kinds are named, and take
 * them away again when they are not — all but the Things `stillReferenced` answers, which the page
 * goes on showing: a Thing a choice compares with is held by that relationship once saved, and until
 * then by the drawing alone.
 *
 * The addition is made again each time the subscription reopens, because a reopened subscription
 * starts from the page's own declaration and its snapshot replaces the store.
 */
export function useWiderList(kindIds: string[] | null, stillReferenced: () => Set<string>): void {
  const { on } = useSse();
  const key = kindIds && kindIds.length > 0 ? JSON.stringify(kindIds) : null;
  const referenced = useRef(stillReferenced);
  useEffect(() => { referenced.current = stillReferenced; });

  useEffect(() => {
    if (!key) return;
    const typeIds = JSON.parse(key) as string[];
    let brought = NOTHING;
    let over = false;

    const widen = async () => {
      brought = NOTHING;
      try {
        const covered = await addToSubscription({ typeIds });
        if (!covered) return;
        brought = broughtIn(covered);
        if (over) {
          const unwanted = toTakeAway(brought, new Set());
          const ids = [...unwanted.things, ...unwanted.relationships];
          if (ids.length > 0) await removeFromSubscription(ids);
          return;
        }
        putInStore(covered);
      } catch (error) {
        console.warn('The wider list could not be read:', error instanceof Error ? error.message : error);
      }
    };

    void widen();
    const off = on(SUBSCRIPTION_OPENED, () => { void widen(); });
    return () => {
      over = true;
      off();
      const unwanted = toTakeAway(brought, referenced.current());
      const ids = [...unwanted.things, ...unwanted.relationships];
      if (ids.length === 0) return;
      useModelStore.getState().applyBatch({ thingRemovals: unwanted.things, relationshipRemovals: unwanted.relationships });
      void removeFromSubscription(ids).catch(() => {});
    };
  }, [key, on]);
}
