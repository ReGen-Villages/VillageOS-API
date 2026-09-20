import { create } from 'zustand';

export interface ToastItem {
  id: number;
  type: 'success' | 'error' | 'warning' | 'information';
  message: string;
}

interface ToastStore {
  toasts: ToastItem[];
  add: (type: ToastItem['type'], message: string) => void;
  remove: (id: number) => void;
}

let nextId = 0;
export const useToastStore = create<ToastStore>((set) => ({
  toasts: [],
  add: (type, message) => {
    const id = nextId++;
    set((s) => ({ toasts: [...s.toasts, { id, type, message }] }));
    if (type !== 'error') {
      setTimeout(() => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })), type === 'warning' ? 5000 : 3000);
    }
  },
  remove: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));

export const toast = {
  success: (message: string) => useToastStore.getState().add('success', message),
  error: (message: string) => useToastStore.getState().add('error', message),
  warning: (message: string) => useToastStore.getState().add('warning', message),
  information: (message: string) => useToastStore.getState().add('information', message),
};
