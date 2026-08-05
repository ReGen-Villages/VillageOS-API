import { X } from 'lucide-react';
import clsx from 'clsx';
import { useToastStore } from './toastStore';

const typeStyles: Record<string, string> = {
  success: 'bg-emerald-600 text-white',
  error: 'bg-red-600 text-white',
  warning: 'bg-amber-500 text-white',
  info: 'bg-blue-600 text-white',
};

export function ToastContainer() {
  const { toasts, remove } = useToastStore();
  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col gap-2">
      {toasts.map((t) => (
        <div key={t.id} className={clsx('flex items-center gap-2 rounded-lg px-4 py-2 shadow-lg text-sm', typeStyles[t.type])}>
          <span className="flex-1">{t.message}</span>
          <button onClick={() => remove(t.id)} className="hover:opacity-70">
            <X size={14} />
          </button>
        </div>
      ))}
    </div>
  );
}
