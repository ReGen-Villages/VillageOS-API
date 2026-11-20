/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * A slot the subscription does not collect draws empty, or without the Thing it names, and reads as a
 * value the model does not have, with nothing to say so. The compiler cannot catch it, because a missing
 * element is a shorter array rather than a type error, and a type has nothing to read at run time, so this
 * reads both files as source. Each slot is looked for in its own widget's switch arm, because slot names
 * repeat across holders and a search of the whole file would find a namesake of a dropped one.
 */

const API_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const TENDRIL = join(API_DIRECTORY, '../../../vos.Tendril/src');
const VOCABULARY = join(TENDRIL, 'types/dashboard.ts');
const COLLECTOR = join(TENDRIL, 'api/pageSelector.ts');

const INTERFACE = /export interface (\w+)\s*\{([\s\S]*?)\n\}/g;
/** `value: Binding`, `origin?: Binding`, `rows: Binding[]`, `spark?: Binding | null`. */
const BINDING_FIELD = /^\s*(\w+)\??\s*:\s*Binding(?:\[\])?\s*(?:\|[^;]*)?;/gm;
/** A field holding other declared types, such as `rows: BulletRow[]`, whose slots the widget's own arm must reach. */
const NESTED_HOLDER_FIELD = /^\s*\w+\??\s*:\s*(\w+)(?:\[\])?\s*;/gm;
/** Also names the switch arm that must read the widget's slots. */
const WIDGET_TYPE = /^\s*type\s*:\s*'([^']+)'\s*;/m;
/** Read from the union, so a type that is not a widget but has a `type` field is not taken for one. */
const WIDGET_UNION = /export type Widget =([\s\S]*?);/;
const UNION_MEMBER = /\|\s*(\w+)/g;

const WIDGET_BINDINGS_FUNCTION = /function widgetBindings\([\s\S]*?\n\}/;
const ARM = /case '([^']+)':([\s\S]*?)(?=\n\s*case '|\n\s*\})/g;

interface Holder {
  name: string;
  bindingSlots: string[];
  nestedHolders: string[];
  widgetType: string | undefined;
}

function declaredHolders(vocabulary: string): Map<string, Holder> {
  const holders = new Map<string, Holder>();
  for (const [, name, body] of vocabulary.matchAll(INTERFACE)) {
    holders.set(name, {
      name,
      bindingSlots: [...body.matchAll(BINDING_FIELD)].map(([, slot]) => slot),
      nestedHolders: [...body.matchAll(NESTED_HOLDER_FIELD)].map(([, target]) => target),
      widgetType: body.match(WIDGET_TYPE)?.[1],
    });
  }
  return holders;
}

function widgetUnionMembers(vocabulary: string): string[] {
  const union = vocabulary.match(WIDGET_UNION)?.[1] ?? '';
  return [...union.matchAll(UNION_MEMBER)].map(([, name]) => name);
}

function collectorArms(collector: string): Map<string, string> {
  const bindings = collector.match(WIDGET_BINDINGS_FUNCTION)?.[0] ?? '';
  return new Map([...bindings.matchAll(ARM)].map(([, widgetType, body]) => [widgetType, body]));
}

function reads(source: string, slot: string): boolean {
  return new RegExp(`\\.\\s*${slot}\\b`).test(source);
}

describe('the subscription a dashboard opens', () => {
  const vocabulary = readFileSync(VOCABULARY, 'utf8');
  const collector = readFileSync(COLLECTOR, 'utf8');

  const holders = declaredHolders(vocabulary);
  const widgets = widgetUnionMembers(vocabulary)
    .map((name) => holders.get(name))
    .filter((holder): holder is Holder => !!holder);
  const arms = collectorArms(collector);

  const nestedInAWidget = new Set(widgets.flatMap((widget) => widget.nestedHolders));

  /** The widget's own slots and those of the holders it lists, which its arm reaches through. */
  function slotsOwedBy(widget: Holder): { owner: string; slot: string }[] {
    const own = widget.bindingSlots.map((slot) => ({ owner: widget.name, slot }));
    const nested = widget.nestedHolders.flatMap((target) =>
      (holders.get(target)?.bindingSlots ?? []).map((slot) => ({ owner: target, slot })),
    );
    return [...own, ...nested];
  }

  // If the vocabulary changes past what the patterns above read, every assertion below passes on nothing.
  it('finds the widgets and binding slots the specification vocabulary declares', () => {
    expect(widgets.length, `no members of the Widget union matched in ${VOCABULARY}`).toBeGreaterThan(0);
    expect([...arms.keys()].sort(), 'the collector has an arm for a widget the union does not list, or the other way about')
      .toEqual(widgets.map((widget) => widget.widgetType).sort());

    const everySlot = [...holders.values()].flatMap((holder) =>
      holder.bindingSlots.map((slot) => `${holder.name}.${slot}`),
    );
    expect(everySlot).toContain('KpiWidget.origin');
  });

  it('reads every slot of a widget in the arm that collects it', () => {
    const unread = widgets.flatMap((widget) =>
      slotsOwedBy(widget)
        .filter(({ slot }) => !reads(arms.get(widget.widgetType!) ?? '', slot))
        .map(({ owner, slot }) => `${owner}.${slot} (arm '${widget.widgetType}')`),
    );

    expect(unread,
      'these slots take a binding and the arm collecting their widget never reads them, so a page asking for them is sent nothing')
      .toEqual([]);
  });

  it('reads the slots of a holder no widget lists', () => {
    const outsideArms = [...arms.values()].reduce((rest, arm) => rest.replace(arm, ''), collector);
    const unread = [...holders.values()]
      .filter((holder) => !holder.widgetType && !nestedInAWidget.has(holder.name))
      .flatMap((holder) =>
        holder.bindingSlots
          .filter((slot) => !reads(outsideArms, slot))
          .map((slot) => `${holder.name}.${slot}`),
      );

    expect(unread,
      'these slots take a binding and no widget arm reaches their holder, so the collector has to read them itself and does not')
      .toEqual([]);
  });
});
