import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient } from '../api/client';
import { useAuth } from '../hooks/useAuth';
import { thingApi } from '../api/thingApi';
import { useUiStore } from '../stores/uiStore';
import { useModelStore } from '../stores/modelStore';
import { reloadModelData } from '../hooks/useModelData';
import { NodeDetailPanel } from '../components/panels/NodeDetailPanel';
import { ResizablePanel } from '../components/panels/ResizablePanel';
import { TypeFilterPanel } from '../components/panels/TypeFilterPanel';
import { toast } from '../components/common/toastStore';
import { IfcUploadDropzone } from '../components/model/IfcUploadDropzone';
import type { VosThing } from '../types/vos';
import type { BimFragmentsMapping } from '../components/model/BimFragmentsViewer';
import { sceneVisibilityFor } from '../components/model/sceneVisibility';
import { ifcGlobalIdOf } from '../utils/ifcIdentity';

const BimFragmentsViewer = lazy(() =>
  import('../components/model/BimFragmentsViewer').then((m) => ({ default: m.BimFragmentsViewer })),
);

type BimFragmentsState =
  | { status: 'loading' }
  | { status: 'empty' }
  | { status: 'error'; message: string }
  | { status: 'ready'; bytes: ArrayBuffer };

// Build the IFC-GlobalId → VosThing-Id map by scanning the Mycelium's authoritative
// thing list rather than the pre-baked .mapping.json sidecar (which drifts whenever
// the seed is regenerated — Bug #5298 follow-up).
function buildMappingFromThings(things: VosThing[]): BimFragmentsMapping {
  const map: BimFragmentsMapping = {};
  for (const t of things) {
    const ifcId = ifcGlobalIdOf(t);
    if (ifcId !== null) map[ifcId] = t.Id;
  }
  return map;
}

export function ModelPage() {
  const { t } = useTranslation();
  const { modelId } = useAuth();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  // Tagged with the model it answers for, so switching models reads as loading without an effect
  // having to write that first — which would render the old model's fragments for a frame.
  const [loadedFragments, setLoadedFragments] = useState<{ modelId: string | null; state: BimFragmentsState }>(
    { modelId: null, state: { status: 'loading' } },
  );
  const bimFragments: BimFragmentsState =
    loadedFragments.modelId === modelId ? loadedFragments.state : { status: 'loading' };
  const [fetchedThing, setFetchedThing] = useState<VosThing | null>(null);

  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const selectNode = useUiStore((s) => s.selectNode);
  const hiddenTypeIds = useUiStore((s) => s.hiddenTypeIds);

  // Memoised because the viewer re-applies visibility across the whole model
  // whenever this changes identity.
  const visibility = useMemo(
    () => sceneVisibilityFor(things, relationships, hiddenTypeIds),
    [things, relationships, hiddenTypeIds],
  );

  // Re-fetch the .frag whenever the JWT-scoped model changes (e.g. via
  // /api/auth/switch-model). The thing/relationship arrays come from the
  // app-shell-level useModelData hook (Feature #5329).
  useEffect(() => {
    let cancelled = false;
    selectNode(null);

    apiClient.getBytes('/api/model/bim/fragments')
      .then((bytes) => {
        if (cancelled) return;
        setLoadedFragments({ modelId, state: bytes === null ? { status: 'empty' } : { status: 'ready', bytes } });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setLoadedFragments({
          modelId,
          state: { status: 'error', message: err instanceof Error ? err.message : t('modelPage.loadFailed') },
        });
      });
    return () => {
      cancelled = true;
    };
  }, [modelId, selectNode, t]);

  // Fetch the full thing (with inherited properties) when selection changes —
  // matches the GraphPage pattern so NodeDetailPanel sees the same shape from both views.
  useEffect(() => {
    if (!selectedNodeId) return;
    let cancelled = false;
    (async () => {
      try {
        const thing = await thingApi.get(selectedNodeId);
        if (!cancelled) setFetchedThing(thing);
      } catch {
        if (!cancelled) setFetchedThing(things.find((t) => t.Id === selectedNodeId) ?? null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [selectedNodeId, things]);

  // Follows the selection, so it is not state: nothing to hold that the selection does not say.
  const detailThing = selectedNodeId ? fetchedThing : null;

  const mapping = useMemo(() => buildMappingFromThings(things), [things]);
  const thingMap = useMemo(() => new Map(things.map((t) => [t.Id, t])), [things]);
  const handlePick = useCallback((vosGuid: string | null) => selectNode(vosGuid), [selectNode]);

  const handleDeleteProperty = useCallback(async (thingId: string, propertyName: string) => {
    try {
      await thingApi.deleteProperty(thingId, propertyName);
      toast.success(t('graph.toast.propertyDeleted', { name: propertyName }));
      await reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.deleteFailed'));
    }
  }, [t]);

  return (
    <div className="h-full flex flex-col p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-white">{t('modelPage.title')}</h1>
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          {t('modelPage.subtitle')}
        </p>
      </header>

      {bimFragments.status === 'ready' ? (
        <div className="flex-1 flex min-h-0 rounded-lg overflow-hidden border border-zinc-200 dark:border-zinc-700 relative">
          <div className="flex-1 min-w-0 relative">
            <Suspense fallback={<LoadingPlaceholder />}>
              <BimFragmentsViewer
                bimFragmentsBytes={bimFragments.bytes}
                mapping={mapping}
                onPick={handlePick}
                visibility={visibility}
              />
            </Suspense>
            {/* Feature #5362 — type filter overlays the 3D viewport.
                Bug #5388: container is a bounded flex column so the panel's
                inner list gets a finite height to scroll within. */}
            <div className="absolute top-3 left-3 z-10 w-72 max-w-[80vw] flex flex-col max-h-[calc(100vh-1.5rem)]">
              <TypeFilterPanel />
            </div>
          </div>
          {detailThing && (
            <ResizablePanel>
              <NodeDetailPanel
                thing={detailThing}
                relationships={relationships}
                allThings={thingMap}
                onClose={() => selectNode(null)}
                onSelectNode={selectNode}
                onDeleteProperty={handleDeleteProperty}
                onRenamed={() => reloadModelData()}
              />
            </ResizablePanel>
          )}
        </div>
      ) : bimFragments.status === 'loading' ? (
        <LoadingPlaceholder />
      ) : bimFragments.status === 'error' ? (
        <ErrorPlaceholder message={bimFragments.message} />
      ) : (
        <EmptyPlaceholder />
      )}
    </div>
  );
}

function LoadingPlaceholder() {
  const { t } = useTranslation();
  return (
    <div
      role="status"
      aria-label={t('modelPage.loadingAria')}
      data-testid="model-viewer-loading"
      className="flex-1 rounded-lg border-2 border-dashed border-zinc-300 dark:border-zinc-700 flex items-center justify-center text-sm text-zinc-500 dark:text-zinc-400"
    >
      {t('modelPage.loading')}
    </div>
  );
}

function EmptyPlaceholder() {
  const { t } = useTranslation();
  return (
    <div
      role="region"
      aria-label={t('modelPage.placeholderAria')}
      data-testid="model-viewer-placeholder"
      className="flex-1 rounded-lg border-2 border-dashed border-zinc-300 dark:border-zinc-700 flex flex-col items-center justify-center text-center p-8"
    >
      <p className="text-lg font-medium text-zinc-700 dark:text-zinc-300">
        {t('modelPage.noModelTitle')}
      </p>
      <p className="text-sm text-zinc-500 dark:text-zinc-500 mt-2 max-w-md">
        {t('modelPage.noModelBody')}
      </p>
      <IfcUploadDropzone />
    </div>
  );
}

function ErrorPlaceholder({ message }: { message: string }) {
  const { t } = useTranslation();
  return (
    <div
      role="alert"
      data-testid="model-viewer-error"
      className="flex-1 rounded-lg border-2 border-dashed border-red-400 dark:border-red-600 flex flex-col items-center justify-center text-center p-8"
    >
      <p className="text-lg font-medium text-red-700 dark:text-red-400">
        {t('modelPage.errorTitle')}
      </p>
      <p className="text-sm text-zinc-500 dark:text-zinc-400 mt-2 max-w-md break-words">{message}</p>
    </div>
  );
}
