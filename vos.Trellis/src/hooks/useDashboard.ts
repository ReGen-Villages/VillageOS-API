import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import {
  discoverDashboardsFromIndex,
  modelIndexFor,
  type DashboardDescriptor,
  type ModelIndex,
  type ModelReads,
  type ResolveContext,
} from '@tendril-ui/tendril';
import { useModelStore } from '../stores/modelStore';
import { usePlatformPagesStore } from '../stores/platformPagesStore';
import { useUiStore } from '../stores/uiStore';

/** Shared rather than built per component, so the navigation and its page read one index instead of
 *  each walking the whole model. */
export function useModelIndex(): ModelIndex {
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  return modelIndexFor(things, relationships);
}

/** The platform's pages come first, because they are the same for every model a session opens, and a page
 *  that moved when the model changed would be lost to its reader. */
export function useDashboards(): DashboardDescriptor[] {
  const declared = usePlatformPagesStore((s) => s.pages);
  const { i18n } = useTranslation();
  const published = discoverDashboardsFromIndex(useModelIndex(), i18n.language);
  return useMemo(() => [...declared, ...published], [declared, published]);
}

const warnInTheConsole = (warning: string) => console.warn(warning);

/** `makeReads` is called once per refresh, because what it builds shares that refresh's reads, so widgets
 *  asking the same question ask once. It is rebuilt whenever anything that could change an answer moves,
 *  so no answer outlives the model it came from; `useBinding` decides per binding which widgets resolve
 *  again. A factory also keeps this file off the broker, so the public page with no credential can use it. */
export function useResolveContext(
  index: ModelIndex,
  scopeId: string | null,
  compareArchetype: string | undefined,
  makeReads: () => ModelReads,
  nonce = 0,
  serverRefresh = 0,
  wrote?: () => void,
): ResolveContext {
  const stateVersions = useUiStore((s) => s.stateVersions);
  const loadsApplied = useModelStore((s) => s.loadsApplied);
  const { i18n } = useTranslation();
  const language = i18n.language;
  return useMemo(
    () => ({ index, scopeId, compareArchetype, nonce, serverRefresh, stateVersions, loadsApplied, reads: makeReads(), wrote, language, warn: warnInTheConsole }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [index, scopeId, compareArchetype, nonce, serverRefresh, stateVersions, loadsApplied, wrote, language],
  );
}
