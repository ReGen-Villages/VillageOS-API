import type { ReactNode } from 'react';

/** `min-w-0` keeps the card in its column, because a grid item's minimum width is otherwise its content's, and
 *  a table with one long cell would take the page's width; bounded, the widget scrolls within the card. The
 *  section bounds its columns too (`OperationsPage.Section`). */
export function WidgetCard({
  title,
  hint,
  right,
  children,
}: {
  title?: string;
  hint?: string;
  right?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className="min-w-0 bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      {(title || right) && (
        <div className="flex flex-wrap items-center justify-between gap-2 mb-1">
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
