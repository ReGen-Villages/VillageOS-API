export interface WireData {
  fromPath?: string;
  toPath?: string;
  transform?: string;
  takenOn?: string[];
}

export function pathLabel(fromPath?: string, toPath?: string, transform?: string): string | undefined {
  if (!fromPath && !toPath && !transform) return undefined;
  const paths = fromPath || toPath ? `${fromPath || '·'} → ${toPath || '·'}` : '';
  return transform ? `${paths} ƒ`.trim() : paths;
}

export function wireDrawing(
  data: WireData, takenOn: string[], failId: string | undefined, choiceNames: Map<string, string>,
): { label?: string; style?: { strokeDasharray: string } } {
  const chosen = takenOn.flatMap((id) => choiceNames.get(id) ?? []).join(', ');
  const label = [chosen, pathLabel(data.fromPath, data.toPath, data.transform)].filter(Boolean).join(' · ') || undefined;
  return { label, style: failId && takenOn.includes(failId) ? { strokeDasharray: '6 4' } : undefined };
}

/** A wire taken on a choice that is gone would point at nothing, so it loses that marking with the choice. */
export function withoutChoices<E extends { source: string; data?: unknown }>(edges: E[], nodeId: string, removed: Set<string>): E[] {
  return edges.map((edge) => {
    const takenOn = (edge.data as WireData | undefined)?.takenOn;
    if (edge.source !== nodeId || !takenOn?.some((id) => removed.has(id))) return edge;
    return { ...edge, data: { ...(edge.data as WireData), takenOn: takenOn.filter((id) => !removed.has(id)) } };
  });
}
