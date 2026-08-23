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
 * asks whether every slot the vocabulary declares is named in the collector — the same shape as the
 * guard on pages declaring their subscription.
 *
 * Source text rather than reflection, because a type has none at run time.
 */

const API = dirname(fileURLToPath(import.meta.url));
const VOCABULARY = join(API, '..', 'types', 'dashboard.ts');
const COLLECTOR = join(API, 'dashboardSubscription.ts');

/** The interfaces whose binding slots the subscription is responsible for. `DashboardSpec` and the
 *  `Binding` union itself are not among them: a binding nested inside another is `withNested`'s to
 *  find, and it is read from the same source below. */
const SLOT_HOLDERS = /export interface (\w*(?:Widget|Row|Stage|Bucket|Column))\s*\{([\s\S]*?)\n\}/g;

/** A field whose declared type is a Binding — `value: Binding`, `origin?: Binding`. */
const BINDING_FIELD = /^\s*(\w+)\??\s*:\s*Binding\s*;/gm;

function declaredSlots(): { holder: string; slot: string }[] {
  const vocabulary = readFileSync(VOCABULARY, 'utf8');
  const found: { holder: string; slot: string }[] = [];
  for (const [, holder, body] of vocabulary.matchAll(SLOT_HOLDERS)) {
    for (const [, slot] of body.matchAll(BINDING_FIELD)) found.push({ holder, slot });
  }
  return found;
}

describe('the subscription a dashboard opens', () => {
  const slots = declaredSlots();
  const collector = readFileSync(COLLECTOR, 'utf8');

  // If the vocabulary is renamed or restructured past what the pattern above reads, this test finds
  // nothing and every assertion below passes vacuously. Saying so is the difference between a guard
  // and a decoration.
  it('finds the binding slots the spec vocabulary declares', () => {
    expect(slots.length, `no binding slots matched in ${VOCABULARY}`).toBeGreaterThan(0);
    expect(slots.map((s) => `${s.holder}.${s.slot}`)).toContain('KpiWidget.origin');
  });

  it('collects every one of them', () => {
    const uncollected = slots.filter(({ slot }) => !new RegExp(`\\.\\s*${slot}\\b`).test(collector));

    expect(uncollected.map((s) => `${s.holder}.${s.slot}`),
      'these slots take a binding and the subscription never reads them, so a page asking for them is sent nothing')
      .toEqual([]);
  });
});
