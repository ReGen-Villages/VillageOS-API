import { FragmentsModels as BimFragmentsModels } from '@thatopen/fragments';
import bimFragmentsWorkerUrl from '@thatopen/fragments/worker?worker&url';

// The library's own getWorker() downloads this file from a public package site each time the viewer opens,
// so the copy the bundler emits beside the page is handed to it instead.
export function startBimFragments(): BimFragmentsModels {
  return new BimFragmentsModels(bimFragmentsWorkerUrl);
}
