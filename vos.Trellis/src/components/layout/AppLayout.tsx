import { useEffect } from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { ToastContainer } from '../common/Toast';
import { useSse } from '../../hooks/useSse';
import { useActivityStore } from '../../stores/activityStore';
import type { ActivityEvent } from '../../types/mycelium';

export function AppLayout() {
  const { on } = useSse();
  const pushEvent = useActivityStore((s) => s.pushEvent);

  useEffect(() => {
    return on('ActivityEvent', (event: unknown) => {
      // The SSE stream may send camelCase or PascalCase depending on Mycelium's
      // AddJsonProtocol config — accept both.
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
