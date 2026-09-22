/**
 * The closing view of an exploration: the parcel on the imagery the model declares, with the sections
 * the submitted page declares as tabs in a sheet along the bottom. Each tab draws its own section
 * through the same widgets the report draws, so a figure added to a tab in the model appears here
 * with no change.
 */
import { Suspense, lazy, useEffect, useState } from 'react';
import { X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { ResolveContext } from '../api/dashboardApi';
import { DashboardSections } from '../components/dashboard/DashboardSections';
import { useElementWidth } from '../hooks/useElementWidth';
import { useMapStore } from '../stores/mapStore';
import type { BasemapSource } from '../types/basemap';
import type { DashboardSection } from '../types/dashboard';
import type { BoundaryPoint } from '../utils/parcelGeometry';
import { imagerySource } from './overviewState';

const MapView = lazy(() => import('../components/map/MapView').then((m) => ({ default: m.MapView })));

const WIDE = 720;

export function OverviewView({
  tabs,
  context,
  sources,
  centre,
  zoom,
  boundary,
  onClose,
}: {
  tabs: DashboardSection[];
  context: ResolveContext;
  sources: BasemapSource[];
  centre: BoundaryPoint;
  zoom: number;
  boundary: readonly BoundaryPoint[];
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [measure, width] = useElementWidth();
  const [open, setOpen] = useState(0);
  const active = tabs[Math.min(open, tabs.length - 1)];

  // The map's layer is the reader's choice and outlives this view; the view opens on the imagery and
  // hands the choice back on closing.
  useEffect(() => {
    const before = useMapStore.getState().selectedSourceName;
    const imagery = imagerySource(sources);
    if (imagery !== null) useMapStore.getState().selectSource(imagery.name);
    return () => useMapStore.setState({ selectedSourceName: before });
  }, [sources]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-50 flex flex-col bg-zinc-950">
      <div className="relative flex-1 min-h-0">
        <Suspense fallback={null}>
          <MapView
            latitude={centre.latitude}
            longitude={centre.longitude}
            sources={sources}
            initialZoom={zoom}
            boundary={boundary}
          />
        </Suspense>
        <button
          type="button"
          onClick={onClose}
          aria-label={t('explore.overview.close')}
          className="absolute top-3 right-3 z-10 flex items-center gap-1.5 px-3 py-1.5 text-sm rounded-md bg-white/90 dark:bg-zinc-900/90 text-zinc-900 dark:text-zinc-100 shadow hover:bg-white dark:hover:bg-zinc-900"
        >
          <X size={14} />
          {t('explore.overview.close')}
        </button>
      </div>

      <div className="max-h-[45vh] flex flex-col bg-white dark:bg-zinc-900 border-t border-zinc-200 dark:border-zinc-800 rounded-t-xl shadow-2xl">
        <div role="tablist" aria-label={t('explore.overview.title')} className="flex overflow-x-auto border-b border-zinc-200 dark:border-zinc-800">
          {tabs.map((tab, at) => {
            const selected = tab === active;
            return (
              <button
                key={tab.tab}
                type="button"
                role="tab"
                aria-selected={selected}
                onClick={() => setOpen(at)}
                className={`shrink-0 px-4 py-3 text-sm font-medium border-b-2 ${
                  selected
                    ? 'border-emerald-600 text-emerald-700 dark:text-emerald-400'
                    : 'border-transparent text-zinc-500 dark:text-zinc-400 hover:text-zinc-800 dark:hover:text-zinc-200'
                }`}
              >
                {tab.title}
              </button>
            );
          })}
        </div>
        <div ref={measure} role="tabpanel" className="overflow-y-auto p-4">
          {active !== undefined && (
            <DashboardSections
              sections={[active]}
              context={context}
              isWide={width >= WIDE}
              whenEmpty={null}
            />
          )}
        </div>
      </div>
    </div>
  );
}
