import type { ReactNode } from 'react';
import { LanguageSwitcher } from './LanguageSwitcher';
import { ToastContainer } from './Toast';

const COLUMN_CLASSES = { wide: 'max-w-5xl', narrow: 'max-w-2xl' } as const;

export function StandalonePage({ icon, title, intro, width, children }: {
  icon: ReactNode;
  title: string;
  intro: string;
  width: keyof typeof COLUMN_CLASSES;
  children: ReactNode;
}) {
  const column = COLUMN_CLASSES[width];
  return (
    <div className="min-h-screen bg-zinc-50 dark:bg-zinc-950">
      <header className={`${column} mx-auto flex items-center gap-3 px-6 pt-10 pb-4`}>
        <div className="w-9 h-9 rounded-lg grid place-items-center text-white bg-gradient-to-br from-emerald-600 to-teal-500">
          {icon}
        </div>
        <div className="flex-1">
          <h1 className="text-xl font-bold text-zinc-900 dark:text-white leading-tight">{title}</h1>
          <p className="text-xs text-zinc-500 dark:text-zinc-400">{intro}</p>
        </div>
        <LanguageSwitcher />
      </header>

      <main className={`${column} mx-auto px-6 pb-16`}>{children}</main>

      <ToastContainer />
    </div>
  );
}
