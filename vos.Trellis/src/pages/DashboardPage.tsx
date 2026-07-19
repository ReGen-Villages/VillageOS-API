import { useEffect, useState, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { ModelStatsCard } from '../components/dashboard/ModelStatsCard';
import { ServicesPanel } from '../components/dashboard/ServicesPanel';
import { ActivityFeed } from '../components/dashboard/ActivityFeed';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import { myceliumApi } from '../api/myceliumApi';
import { endpointApi } from '../api/endpointApi';
import { thingApi } from '../api/thingApi';
import { useSse } from '../hooks/useSse';
import { useActivityStore } from '../stores/activityStore';
import { useModelStore } from '../stores/modelStore';
import { toast } from '../components/common/Toast';
import { PropertyModePanel } from '../components/dashboard/PropertyModePanel';
import { Power, PanelRightOpen, LogOut, ArrowLeftRight, FileCode2, RefreshCw } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import { RegenLogo } from '../components/auth/RegenLogo';

import type { RegisteredService, EndpointServiceInfo } from '../types/mycelium';

const FEED_COLLAPSED_KEY = 'vos-activity-feed-collapsed';

export function DashboardPage() {
  const navigate = useNavigate();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const [services, setServices] = useState<RegisteredService[]>([]);
  const [endpointServices, setEndpointServices] = useState<EndpointServiceInfo[]>([]);
  const [httpOk, setHttpOk] = useState(false);
  const [showShutdown, setShowShutdown] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<{ thingId: string; name: string } | null>(null);
  const [feedCollapsed, setFeedCollapsed] = useState(() => localStorage.getItem(FEED_COLLAPSED_KEY) === 'true');
  const events = useActivityStore((s) => s.events);
  const { on, connected } = useSse();
  const { logout, switchModel } = useAuth();

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
    loadMyceliumData();
  }, [loadMyceliumData]);

  // On connection drop, mark services offline so no stale "running" state shows.
  useEffect(() => {
    if (!connected) {
      setServices((prev) =>
        prev.map((s) => ({ ...s, IsRunning: false, ProcessId: undefined, HealthStatus: 'Unreachable' })),
      );
    }
  }, [connected]);

  // Mycelium-specific live updates only; model data is handled at app level.
  useEffect(() => {
    const unsubs = [
      on('ServiceHealthChanged', () => myceliumApi.getServices().then(setServices)),
      on('DaemonStatusChanged', () => myceliumApi.getServices().then(setServices)),
      on('ServiceRequestCompleted', () => myceliumApi.getServices().then(setServices)),
      on('EndpointServiceRequestCompleted', () => endpointApi.getAll().then(setEndpointServices)),
      on('ModelChanged', () => loadMyceliumData()),
    ];
    return () => unsubs.forEach((u) => u());
  }, [on, loadMyceliumData]);

  const handleStartService = async (id: string) => {
    try {
      await myceliumApi.startService(id);
      toast.success('Service started');
      setServices(await myceliumApi.getServices());
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Start failed');
    }
  };

  const handleStopService = async (id: string) => {
    try {
      await myceliumApi.stopService(id);
      toast.success('Stop requested');
      setServices(await myceliumApi.getServices());
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Stop failed');
    }
  };

  const handleDeleteService = async () => {
    if (!deleteTarget) return;
    const { thingId } = deleteTarget;
    setDeleteTarget(null);
    try {
      await thingApi.remove(thingId);
      toast.success('Service retracted from model');
      await loadMyceliumData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
  };

  const handleReloadSeeds = async () => {
    try {
      await myceliumApi.reloadSeeds();
      toast.success('Seeds reloaded from disk');
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Reload failed');
    }
  };

  const handleShutdown = async () => {
    setShowShutdown(false);
    try {
      await myceliumApi.shutdown();
      toast.success('Mycelium shutdown initiated');
      setHttpOk(false);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Shutdown failed');
    }
  };

  return (
    <div className="h-full flex flex-col">
      <div className="flex items-center justify-between px-6 pt-6 pb-4 flex-shrink-0">
        <div className="flex items-center gap-3">
          <RegenLogo className="w-8 h-8" />
          <h2 className="text-xl font-bold">Mycelium Dashboard</h2>
        </div>
        <div className="flex items-center gap-4 text-xs">
          <div className="flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full ${httpOk ? 'bg-emerald-500' : 'bg-red-500'}`} />
            <span className="text-zinc-500">Mycelium</span>
          </div>
          <div className="flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-500' : 'bg-red-500'}`} />
            <span className="text-zinc-500">Live</span>
          </div>
          <a
            href={`${import.meta.env.VITE_BROKER_URL || ''}/swagger`}
            target="_blank"
            rel="noopener noreferrer"
            title="Swagger API docs"
            className="ml-2 p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            <FileCode2 size={14} />
          </a>
          <button
            onClick={handleReloadSeeds}
            title="Reload seeds from disk"
            className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            <RefreshCw size={14} />
          </button>
          <button
            onClick={() => setShowShutdown(true)}
            title="Shutdown Mycelium"
            className="p-1.5 rounded hover:bg-red-600/20 text-zinc-500 hover:text-red-400 transition-colors"
          >
            <Power size={14} />
          </button>
          <button
            onClick={switchModel}
            title="Switch model"
            className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            <ArrowLeftRight size={14} />
          </button>
          <button
            onClick={logout}
            title="Log out"
            className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            <LogOut size={14} />
          </button>
          {feedCollapsed && (
            <button
              onClick={toggleFeedCollapsed}
              title="Show activity feed"
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
            <ServicesPanel services={services} endpoints={endpointServices} onStart={handleStartService} onStop={handleStopService} onDelete={(thingId, name) => setDeleteTarget({ thingId, name })} onViewLogs={(serviceKey) => navigate(`/logs?service=${serviceKey}`)} />
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
        title="Shutdown Mycelium"
        message="Are you sure you want to shut down Mycelium? All services and daemons will be stopped. Trellis will lose its connection."
        confirmLabel="Shutdown"
        danger
        onConfirm={handleShutdown}
        onCancel={() => setShowShutdown(false)}
      />

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete service"
        message={`Retract "${deleteTarget?.name}" from the model? This removes the connection so Mycelium no longer routes to it. It stays in the seed, so a seed reload restores it. To only stop the process, use Stop instead.`}
        confirmLabel="Delete"
        danger
        onConfirm={handleDeleteService}
        onCancel={() => setDeleteTarget(null)}
      />
    </div>
  );
}
