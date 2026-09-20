import { useCallback, useState } from 'react';
import type { DashboardDescriptor, DashboardSpecification } from '../types/dashboard';
import { isKeptPage, newPage, openedForDesign } from '../utils/designSpec';
import type { DesignSelection } from '../utils/designEdits';

/** Where the page being designed came from: a Thing the model holds, or nothing yet. */
export interface DesignSource {
  id: string | null;
  name: string;
  /** The seed's, which the designer copies and never writes over. */
  seeded: boolean;
}

export interface Design {
  specification: DashboardSpecification;
  source: DesignSource;
  /** Whether anything has changed since the page was opened or kept. */
  dirty: boolean;
}

/**
 * The page being designed: the specification as it is edited, where it came from, and what is
 * selected on it. The workbench holding this is keyed on the page it opened, so opening another page
 * starts afresh rather than carrying edits across.
 */
export function useDesign(initial: { opened?: DashboardDescriptor & { specification: DashboardSpecification }; started?: string }) {
  const [design, setDesign] = useState<Design>(() =>
    initial.opened
      ? {
          specification: openedForDesign(initial.opened.specification),
          source: { id: initial.opened.id, name: initial.opened.name, seeded: !isKeptPage(initial.opened.specification) },
          dirty: false,
        }
      : { specification: newPage(initial.started ?? ''), source: { id: null, name: initial.started ?? '', seeded: false }, dirty: false },
  );
  const [selection, setSelection] = useState<DesignSelection | null>(null);

  const edit = useCallback((change: (specification: DashboardSpecification) => DashboardSpecification) => {
    setDesign((held) => {
      const specification = change(held.specification);
      return specification === held.specification ? held : { ...held, specification, dirty: true };
    });
  }, []);

  /** The page now stands in the model under this id, as written. */
  const kept = useCallback((id: string, name: string, written: DashboardSpecification) => {
    setDesign({ specification: written, source: { id, name, seeded: false }, dirty: false });
  }, []);

  return { design, selection, select: setSelection, edit, kept };
}
