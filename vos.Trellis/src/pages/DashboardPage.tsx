import { useEffect, useState, useCallback } from 'react';
import { ModelStatsCard } from '../components/dashboard/ModelStatsCard';
import { ServicesPanel } from '../components/dashboard/ServicesPanel';
import { DaemonsPanel } from '../components/dashboard/DaemonsPanel';
import { EndpointServicesPanel } from '../components/dashboard/EndpointServicesPanel';
import { ActivityFeed } from '../components/dashboard/ActivityFeed';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import { myceliumApi } from '../api/myceliumApi';
import { endpointApi } from '../api/endpointApi';
import { useSignalR } from '../hooks/useSignalR';
import { useActivityStore } from '../stores/activityStore';
import { useModelStore } from '../stores/modelStore';
import { toast } from '../components/common/Toast';
import { PropertyModePanel } from '../components/dashboard/PropertyModePanel';
import { Power, PanelRightOpen, LogOut, ArrowLeftRight, FileCode2, RefreshCw } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import { RegenLogo } from '../components/auth/RegenLogo';

import type { RegisteredService, DaemonInfo, EndpointServiceInfo } from '../types/mycelium';

const FEED_COLLAPSED_KEY = 'vos-activity-feed-collapsed';

export function DashboardPage() {
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const [services, setServices] = useState<RegisteredService[]>([]);
  const [daemons, setDaemons] = useState<DaemonInfo[]>([]);
  const [endpointServices, setEndpointServices] = useState<EndpointServiceInfo[]>([]);
  const [httpOk, setHttpOk] = useState(false);
  const [showShutdown, setShowShutdown] = useState(false);
  const [feedCollapsed, setFeedCollapsed] = useState(() => localStorage.getItem(FEED_COLLAPSED_KEY) === 'true');
  const events = useActivityStore((s) => s.events);
  const { on, connected } = useSignalR();
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
      const [s, d, ep] = await Promise.all([
        myceliumApi.getServices(),
        myceliumApi.getDaemons(),
        endpointApi.getAll(),
      ]);
      setServices(s);
      setDaemons(d);
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

  // When connection drops, mark all services/daemons as offline (no stale "running" state)
  useEffect(() => {
    if (!connected) {
      setServices((prev) =>
        prev.map((s) => ({ ...s, IsRunning: false, HealthStatus: 'Unreachable' })),
      );
      setDaemons((prev) => prev.map((d) => ({ ...d, IsRunning: false, ProcessId: undefined })));
    }
  }, [connected]);

  // SignalR live updates (Mycelium-specific only — model data handled at app level)
  useEffect(() => {
    const unsubs = [
      on('ServiceHealthChanged', () => myceliumApi.getServices().then(setServices)),
      on('DaemonStatusChanged', () => myceliumApi.getDaemons().then(setDaemons)),
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

  const handleStopDaemon = async (key: string) => {
    try {
      await myceliumApi.stopDaemon(key);
      toast.success('Daemon stopped');
      setDaemons(await myceliumApi.getDaemons());
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Stop failed');
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
    <div className="h-full overflow-auto p-6">
      <div className="flex items-center justify-between mb-6">
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

      <div className={`grid grid-cols-1 gap-6 ${feedCollapsed ? '' : 'lg:grid-cols-3'}`}>
        <div className={`space-y-6 ${feedCollapsed ? '' : 'lg:col-span-2'}`}>
          <ModelStatsCard things={things} relationships={relationships} />
          <ServicesPanel services={services} onStart={handleStartService} onStop={handleStopService} />
          <EndpointServicesPanel endpoints={endpointServices} />
          <DaemonsPanel daemons={daemons} onStop={handleStopDaemon} />
          <PropertyModePanel />
        </div>
        {!feedCollapsed && (
          <div className="lg:col-span-1 lg:sticky lg:top-6">
            <ActivityFeed events={events} onCollapse={toggleFeedCollapsed} />
          </div>
        )}
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
    </div>
  );
}
