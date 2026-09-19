/**
 * What a binding has to wait for before it is worth resolving again.
 *
 * A page carries figures of two kinds. One is answered from the model the browser has loaded, and
 * costs a walk over an index that is rebuilt as events arrive. The other is answered by the broker,
 * and costs a request. Re-resolving both whenever anything in the model moved is what made one open
 * dashboard ask the platform tens of times a second: a property change anywhere re-asked every
 * question on the page, however unrelated.
 *
 * A figure the broker answers waits on the derived states its own answer is made of. Nothing else
 * the model does can move its number between one cadence and the next.
 */
import type { Binding, ComputedColumn, RelationStep } from '../types/dashboard';

function stepStates(via: RelationStep[] | undefined): string[] {
  return (via ?? []).flatMap((step) => [step.inState, step.notInState].filter((state): state is string => !!state));
}

function columnStates(computed: ComputedColumn[] | undefined): string[] {
  return (computed ?? []).flatMap((column) => statesRead(column.value));
}

/** The derived states this binding's answer is made of, in the order the binding names them. A
 *  binding the broker answers from something other than state membership — a reduction over a
 *  trailing window, a model-side service — names none, and follows the page's cadence alone. A walk
 *  step that keeps or drops what is in a state reads that state, whatever the binding's kind. */
export function statesRead(binding: Binding): string[] {
  switch (binding.kind) {
    case 'stateCount':
      return [binding.state];
    case 'stateList':
      return [binding.state, ...(binding.excludeState ? [binding.excludeState] : []), ...columnStates(binding.computed)];
    case 'stateOf':
      return binding.states;
    case 'verdict':
      return [...binding.states.map((candidate) => candidate.state), ...stepStates(binding.via)];
    case 'related':
    case 'working':
      return stepStates(binding.via);
    case 'origin':
      return [...stepStates(binding.via), ...stepStates(binding.source?.via)];
    case 'ratio':
      return [...statesRead(binding.numerator), ...statesRead(binding.denominator)];
    case 'thingList':
    case 'compareEntities':
      return columnStates(binding.computed);
    default:
      return [];
  }
}

/** Whether answering this binding costs a request. The rest are read from the loaded model. */
export function askedOfTheBroker(binding: Binding): boolean {
  switch (binding.kind) {
    case 'stateCount':
    case 'stateList':
    case 'stateOf':
    case 'verdict':
    case 'timeseries':
    case 'latest':
    case 'service':
    case 'history':
      return true;
    case 'ratio':
      return askedOfTheBroker(binding.numerator) || askedOfTheBroker(binding.denominator);
    case 'thingList':
    case 'compareEntities':
      return (binding.computed ?? []).some((column) => askedOfTheBroker(column.value));
    case 'related':
    case 'working':
    case 'origin':
      return statesRead(binding).length > 0;
    default:
      return false;
  }
}
