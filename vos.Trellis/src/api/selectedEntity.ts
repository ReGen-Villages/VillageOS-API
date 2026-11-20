import { SCOPE_REFERENCE, type Binding, type Widget } from '@tendril-ui/tendril';
import { slotsOf } from './dashboardSubscription';

/** A `property` read off the selected entity averages across all of them when none is selected, and a
 *  service is asked about all of them at once, so neither needs one. */
export function needsOneEntity(binding: Binding): boolean {
  switch (binding.kind) {
    case 'related':
    case 'stateOf':
    case 'verdict':
    case 'origin':
    case 'working':
      return binding.thing === undefined || binding.thing === SCOPE_REFERENCE;
    case 'history':
      return true;
    case 'ratio':
      return needsOneEntity(binding.numerator) || needsOneEntity(binding.denominator);
    default:
      return false;
  }
}

/** The origin line is left out: it only explains a figure, and a figure averaged across the entities has
 *  no one origin to give. */
export function widgetNeedsOneEntity(widget: Widget): boolean {
  return slotsOf(widget).some((binding) => binding.kind !== 'origin' && needsOneEntity(binding));
}
