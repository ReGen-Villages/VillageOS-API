import type { ReactNode } from 'react';

/** Standard Trellis card wrapper used by every dashboard widget.
 *
 *  `min-w-0` is what keeps the card inside the column it was placed in: a grid item's own minimum
 *  width is its content's otherwise, so a widget holding something wide — a table with one long
 *  cell — would push past its column and take the page's width with it. Bounded here, the widget
 *  scrolls within the card instead. */
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
