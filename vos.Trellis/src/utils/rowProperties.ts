import type { DashboardSpec, Widget } from '../types/dashboard';

/**
 * What a state-driven list asks the platform to send beside each row.
 *
 * A row arrives carrying the values its widget reads, so a table is drawn from the answer that
 * listed it. The list is derived from the widget rather than written beside the binding, because a
 * second list is wrong the day somebody adds a column; the Design page applies the rule when a page
 * is kept, and never shows the field.
 */

const ALWAYS_SENT = new Set(['id', 'name']);

function keysUnder(node: unknown, field: string): string[] {
  const held = (node as Record<string, unknown>)[field];
  return Array.isArray(held) ? held.map((entry) => String((entry as { key: string }).key)) : [];
}

function stringsUnder(node: unknown, field: string): string[] {
  const held = (node as Record<string, unknown>)[field];
  return Array.isArray(held) ? held.map(String) : [];
}

/** Every key a widget reads off one of its rows: what its table draws, what its search box matches
 *  on, what it sorts by, what a leaderboard scores and labels with, what an action list titles a row
 *  with and shows beside the name — plus what a field picked from a roster shows beside each name. */
export function keysReadOffARow(widget: Widget): Set<string> {
  const keys = new Set<string>();
  for (const field of ['columns', 'drillColumns', 'metrics']) for (const key of keysUnder(widget, field)) keys.add(key);
  for (const key of stringsUnder(widget, 'searchKeys')) keys.add(key);
  if ('sortKey' in widget && widget.sortKey) keys.add(widget.sortKey);
  if (widget.type === 'leaderboard') {
    if (widget.labelKey) keys.add(widget.labelKey);
    if (widget.sublabelKey) keys.add(widget.sublabelKey);
  }
  if (widget.type === 'action') {
    keys.add(widget.label ?? 'name');
    for (const key of widget.shows ?? []) keys.add(key);
  }
  const asked = [...('fields' in widget ? widget.fields : []), ...('asks' in widget ? widget.asks ?? [] : [])];
  for (const field of asked) for (const key of field.shows ?? []) keys.add(key);
  for (const key of ALWAYS_SENT) keys.delete(key);
  return keys;
}

/** The node with every state list drawn as rows naming what its rows carry. A binding under
 *  `computed` is a column worked out per row and draws no columns of its own, so it is left as it is. */
function withListsNaming(node: unknown, wanted: Set<string>): unknown {
  if (Array.isArray(node)) return node.map((entry) => withListsNaming(entry, wanted));
  if (!node || typeof node !== 'object') return node;
  const held = node as Record<string, unknown>;
  const next: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(held)) next[key] = key === 'computed' ? value : withListsNaming(value, wanted);
  if (held.kind === 'stateList') {
    const derived = new Set(keysUnder(held, 'computed'));
    const properties = [...wanted].filter((key) => !derived.has(key)).sort();
    if (properties.length > 0) next.properties = properties;
    else delete next.properties;
  }
  return next;
}

/** The specification as it is written: marked as designed, with every row list naming what it carries. */
export function readyToKeep(spec: DashboardSpec): DashboardSpec {
  return {
    ...spec,
    designed: true,
    sections: spec.sections.map((section) => ({
      ...section,
      widgets: section.widgets.map((widget) => withListsNaming(widget, keysReadOffARow(widget)) as Widget),
    })),
  };
}
