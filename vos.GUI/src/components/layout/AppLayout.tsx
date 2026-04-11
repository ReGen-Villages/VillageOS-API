import { useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { ToastContainer } from '../common/Toast';
import { useSignalR } from '../../hooks/useSignalR';
import { useActivityStore } from '../../stores/activityStore';
import type { ActivityEvent } from '../../types/broker';

export function AppLayout() {
  const { on } = useSignalR();
  const pushEvent = useActivityStore((s) => s.pushEvent);

  useEffect(() => {
    return on('ActivityEvent', (event: unknown) => {
      // Normalise casing — SignalR sends camelCase by default unless the broker
      // configures AddJsonProtocol with PropertyNamingPolicy = null.
      // Accept both camelCase and PascalCase for robustness.
      const raw = event as Record<string, unknown>;
      const normalised: ActivityEvent = {
        Type: (raw.Type ?? raw.type) as string,
        Timestamp: (raw.Timestamp ?? raw.timestamp) as string,
        Description: (raw.Description ?? raw.description) as string,
        Details: raw.Details ?? raw.details,
      };
      pushEvent(normalised);
    });
  }, [on, pushEvent]);

  return (
    <div className="flex h-dvh bg-white dark:bg-zinc-950 text-zinc-900 dark:text-zinc-100">
      <Sidebar />
      <main className="flex-1 overflow-hidden">
        <Outlet />
      </main>
      <ToastContainer />
    </div>
  );
}
