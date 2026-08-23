import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ModelStatsCard } from '../components/dashboard/ModelStatsCard';
import { ServicesPanel } from '../components/dashboard/ServicesPanel';
import { EngineMetricsPanel } from '../components/dashboard/EngineMetricsPanel';
import { ActivityFeed } from '../components/dashboard/ActivityFeed';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import { myceliumApi } from '../api/myceliumApi';
import { engineMetricsApi } from '../api/engineMetricsApi';
import { endpointApi } from '../api/endpointApi';
import { thingApi } from '../api/thingApi';
import { fetchFullLog } from '../api/logsApi';
import { triggerDownload } from '../utils/logDownload';
import { useSse, useSubscription } from '../hooks/useSse';
import { WHOLE_MODEL } from '../types/subscription';
import { useActivityStore } from '../stores/activityStore';
import { useModelStore } from '../stores/modelStore';
import { toast } from '../components/common/toastStore';
import { PropertyModePanel } from '../components/dashboard/PropertyModePanel';
import { Power, PanelRightOpen, FileCode2, RefreshCw } from 'lucide-react';
import { RegenLogo } from '../components/auth/RegenLogo';

import type { RegisteredService, EndpointServiceInfo } from '../types/mycelium';
import type { EngineMetricsSummary } from '../types/engineMetrics';

const FEED_COLLAPSED_KEY = 'vos-activity-feed-collapsed';

// Definition writes publish EngineConfigurationChanged (#6227), so the panel refreshes on
// events; the poll stays as a fallback for a dropped stream.
const ENGINE_METRICS_POLL_MS = 15000;

export function DashboardPage() {
  useSubscription(WHOLE_MODEL);
  const navigate = useNavigate();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const [services, setServices] = useState<RegisteredService[]>([]);
  const [endpointServices, setEndpointServices] = useState<EndpointServiceInfo[]>([]);
  const [httpOk, setHttpOk] = useState(false);
  const [engineMetrics, setEngineMetrics] = useState<EngineMetricsSummary | null>(null);
  const [showShutdown, setShowShutdown] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<{ thingId: string; name: string } | null>(null);
  const [feedCollapsed, setFeedCollapsed] = useState(() => localStorage.getItem(FEED_COLLAPSED_KEY) === 'true');
  const events = useActivityStore((s) => s.events);
  const { on, connected } = useSse();
  const { t } = useTranslation();

  const toggleFeedCollapsed = useCallback(() => {
    setFeedCollapsed((prev) => {
      const next = !prev;
      localStorage.setItem(FEED_COLLAPSED_KEY, String(next));
      return next;
    });
  }, []);

  const loadMyceliumData = useCallback(async () => {
    try {
      const [s, ep] = await Promise.all([
        myceliumApi.getServices(),
        endpointApi.getAll(),
      ]);
      setServices(s);
      setEndpointServices(ep);
      setHttpOk(true);
    } catch (err) {
      console.warn('Mycelium services load failed (non-fatal):', err);
      setHttpOk(false);
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- every state write in the loader is after an await, so nothing is set while the effect runs; the rule does not model that boundary
    loadMyceliumData();
  }, [loadMyceliumData]);

  const loadEngineMetrics = useCallback(async () => {
    try {
      setEngineMetrics(await engineMetricsApi.getSummary());
    } catch {
      setEngineMetrics(null);
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- state is only set after the await
    loadEngineMetrics();
    const interval = setInterval(loadEngineMetrics, ENGINE_METRICS_POLL_MS);
    return () => clearInterval(interval);
  }, [loadEngineMetrics]);

  // With the connection down, every service reads as unreachable — derived rather than written into
  // state, so a reconnect shows what was last loaded instead of the offline values overwriting it.
  const displayedServices = connected
    ? services
    : services.map((s) => ({ ...s, IsRunning: false, ProcessId: undefined, HealthStatus: 'Unreachable' }));

  // Mycelium-specific live updates only; model data is handled at app level.
  useEffect(() => {
    const unsubs = [
      on('ServiceHealthChanged', () => myceliumApi.getServices().then(setServices)),
      on('DaemonStatusChanged', () => myceliumApi.getServices().then(setServices)),
      on('ServiceRequestCompleted', () => myceliumApi.getServices().then(setServices)),
      on('EndpointServiceRequestCompleted', () => endpointApi.getAll().then(setEndpointServices)),
      on('ModelChanged', () => loadMyceliumData()),
      on('ModelChanged', () => loadEngineMetrics()),
      on('EngineConfigurationChanged', () => loadEngineMetrics()),
    ];
    return () => unsubs.forEach((u) => u());
  }, [on, loadMyceliumData, loadEngineMetrics]);

  const handleDownloadServiceLog = async (serviceKey: string) => {
    try {
      const { blob, fileName } = await fetchFullLog(serviceKey);
      triggerDownload(blob, fileName);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('dashboard.toast.logDownloadFailed'));
    }
  };

  const handleStartService = async (id: string) => {
    try {
      await myceliumApi.startService(id);
      toast.success(t('dashboard.toast.serviceStarted'));
      setServices(await myceliumApi.getServices());
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('dashboard.toast.startFailed'));
    }
  };

  const handleStopService = async (id: string) => {
    try {
      await myceliumApi.stopService(id);
      toast.success(t('dashboard.toast.stopRequested'));
      setServices(await myceliumApi.getServices());
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('dashboard.toast.stopFailed'));
    }
  };

  const handleDeleteService = async () => {
    if (!deleteTarget) return;
    const { thingId } = deleteTarget;
    setDeleteTarget(null);
    try {
      await thingApi.remove(thingId);
      toast.success(t('dashboard.toast.serviceRetracted'));
      await loadMyceliumData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('dashboard.toast.deleteFailed'));
    }
  };

  const handleReloadSeeds = async () => {
    try {
      await myceliumApi.reloadSeeds();
      toast.success(t('dashboard.toast.seedsReloaded'));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('dashboard.toast.reloadFailed'));
    }
  };

  const handleShutdown = async () => {
    setShowShutdown(false);
    try {
      await myceliumApi.shutdown();
      toast.success(t('dashboard.toast.shutdownInitiated'));
      setHttpOk(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('dashboard.toast.shutdownFailed'));
    }
  };

  return (
    <div className="h-full flex flex-col">
      <div className="flex items-center justify-between px-6 pt-6 pb-4 flex-shrink-0">
        <div className="flex items-center gap-3">
          <RegenLogo className="w-8 h-8" />
          <h2 className="text-xl font-bold">{t('dashboard.title')}</h2>
        </div>
        <div className="flex items-center gap-4 text-xs">
          <div className="flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full ${httpOk ? 'bg-emerald-500' : 'bg-red-500'}`} />
            <span className="text-zinc-500">{t('dashboard.status.mycelium')}</span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-500' : 'bg-red-500'}`} />
            <span className="text-zinc-500">{t('dashboard.status.live')}</span>
          </div>
          <a
            href={`${import.meta.env.VITE_BROKER_URL || ''}/swagger`}
            target="_blank"
            rel="noopener noreferrer"
            title={t('dashboard.actions.swagger')}
            className="ml-2 p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            <FileCode2 size={14} />
          </a>
          <button
            onClick={handleReloadSeeds}
            title={t('dashboard.actions.reloadSeeds')}
            className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            <RefreshCw size={14} />
          </button>
          <button
            onClick={() => setShowShutdown(true)}
            title={t('dashboard.actions.shutdown')}
            className="p-1.5 rounded hover:bg-red-600/20 text-zinc-500 hover:text-red-400 transition-colors"
          >
            <Power size={14} />
          </button>
          {feedCollapsed && (
            <button
              onClick={toggleFeedCollapsed}
              title={t('dashboard.actions.showActivityFeed')}
              className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
            >
              <PanelRightOpen size={14} />
            </button>
          )}
        </div>
      </div>

      <div className="flex-1 overflow-auto px-6 pb-6">
        <div className={`grid grid-cols-1 gap-6 ${feedCollapsed ? '' : 'lg:grid-cols-3'}`}>
          <div className={`space-y-6 ${feedCollapsed ? '' : 'lg:col-span-2'}`}>
            <ModelStatsCard things={things} relationships={relationships} />
            <EngineMetricsPanel metrics={connected ? engineMetrics : null} />
            <ServicesPanel services={displayedServices} endpoints={endpointServices} onStart={handleStartService} onStop={handleStopService} onDelete={(thingId, name) => setDeleteTarget({ thingId, name })} onViewLogs={(serviceKey) => navigate(`/logs?service=${serviceKey}`)} onDownloadLogs={handleDownloadServiceLog} />
            <PropertyModePanel />
          </div>
          {!feedCollapsed && (
            <div className="lg:col-span-1 lg:sticky lg:top-0">
              <ActivityFeed events={events} onCollapse={toggleFeedCollapsed} />
            </div>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={showShutdown}
        title={t('dashboard.shutdownDialog.title')}
        message={t('dashboard.shutdownDialog.message')}
        confirmLabel={t('dashboard.shutdownDialog.confirm')}
        danger
        onConfirm={handleShutdown}
        onCancel={() => setShowShutdown(false)}
      />

      <ConfirmDialog
        open={deleteTarget !== null}
        title={t('dashboard.deleteDialog.title')}
        message={t('dashboard.deleteDialog.message', { name: deleteTarget?.name ?? '' })}
        confirmLabel={t('dashboard.deleteDialog.confirm')}
        danger
        onConfirm={handleDeleteService}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
