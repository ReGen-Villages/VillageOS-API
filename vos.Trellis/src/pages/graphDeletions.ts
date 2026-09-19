/** What the graph offers to delete, each named in the confirmation by its own word under `graph.entity`. */
export const DELETABLE_ENTITIES = ['thing', 'relationship'] as const;
export type DeletableEntity = (typeof DELETABLE_ENTITIES)[number];
