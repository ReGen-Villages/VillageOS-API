/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * A widget slot that takes a binding but is not collected by the subscription is a page asking for
 * less than it draws — and nothing says so. The widget resolves its binding against whatever the
 * page happens to hold, so the figure renders empty, or renders without the Thing it names, and
 * reads as a value the model does not have (Bug #6701, where the KPI's `origin` slot was the one
 * left out).
 *
 * TypeScript cannot catch it: the collector is a hand-written switch returning an array, and a
 * missing element is a shorter array, not a type error. So this reads both files as source text and
 * asks whether every slot the vocabulary declares is read where the collector is supposed to read
 * it — the same shape as the guard on pages declaring their subscription.
 *
 * Source text rather than reflection, because a type has none at run time.
 *
 * Each slot is looked for in one switch arm rather than anywhere in the collector, because slot
 * names repeat across holders: `rows` is a binding on a table and a list of row objects on a
 * bullet, and `value` names a slot on three different holders. Searched file-wide, a dropped
 * binding read still finds one of its namesakes, and the guard passes on a page that is already
 * broken.
 */

const API_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const VOCABULARY = join(API_DIRECTORY, '..', 'types', 'dashboard.ts');
const COLLECTOR = join(API_DIRECTORY, 'dashboardSubscription.ts');

const INTERFACE = /export interface (\w+)\s*\{([\s\S]*?)\n\}/g;
/** `value: Binding`, `origin?: Binding`, `rows: Binding[]`, `spark?: Binding | null`. */
const BINDING_FIELD = /^\s*(\w+)\??\s*:\s*Binding(?:\[\])?\s*(?:\|[^;]*)?;/gm;
/** A field holding other declared Things — `rows: BulletRow[]` — whose slots the widget's own arm
 *  is the one that has to reach. */
const NESTED_HOLDER_FIELD = /^\s*\w+\??\s*:\s*(\w+)(?:\[\])?\s*;/gm;
/** The literal a widget answers `widget.type` with, which is also the arm that must read it. */
const WIDGET_TYPE = /^\s*type\s*:\s*'([^']+)'\s*;/m;
/** The union the collector switches over. Read from the union rather than from every interface
 *  carrying a `type` field, so a non-widget that happens to have one is not mistaken for one. */
const WIDGET_UNION = /export type Widget =([\s\S]*?);/;
const UNION_MEMBER = /\|\s*(\w+)/g;

const DISPATCH_FUNCTION = /function widgetBindings\([\s\S]*?\n\}/;
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
  const dispatch = collector.match(DISPATCH_FUNCTION)?.[0] ?? '';
  return new Map([...dispatch.matchAll(ARM)].map(([, widgetType, body]) => [widgetType, body]));
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

  /** Every slot the arm for this widget has to read: the widget's own, and those of the holders it
   *  lists, which the arm reaches through. */
  function slotsOwedBy(widget: Holder): { owner: string; slot: string }[] {
    const own = widget.bindingSlots.map((slot) => ({ owner: widget.name, slot }));
    const nested = widget.nestedHolders.flatMap((target) =>
      (holders.get(target)?.bindingSlots ?? []).map((slot) => ({ owner: target, slot })),
    );
    return [...own, ...nested];
  }

  // If the vocabulary is renamed or restructured past what the patterns above read, this test finds
  // nothing and every assertion below passes vacuously. Saying so is the difference between a guard
  // and a decoration.
  it('finds the widgets and binding slots the spec vocabulary declares', () => {
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
