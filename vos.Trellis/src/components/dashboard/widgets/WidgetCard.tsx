import type { ReactNode } from 'react';

/** Standard Trellis card wrapper used by every dashboard widget. */
export function WidgetCard({
  title,
  hint,
  right,
  children,
  className = '',
}: {
  title?: string;
  hint?: string;
  right?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4 ${className}`}
    >
      {(title || right) && (
        <div className="flex items-center justify-between gap-2 mb-1">
          <h3 className="text-[11px] font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
            {title}
            {hint && (
              <span className="ml-2 normal-case tracking-normal font-normal text-zinc-400 dark:text-zinc-500">
                {hint}
              </span>
            )}
          </h3>
          {right}
        </div>
      )}
      {children}
    </div>
  );
}
