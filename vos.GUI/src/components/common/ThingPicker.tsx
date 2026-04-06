import { useState, useMemo, useRef, useEffect } from 'react';
import type { VosThing } from '../../types/vos';

interface PickerItem {
  Id: string;
  Name: string;
}

interface EntityPickerProps<T extends PickerItem> {
  items: T[];
  value: string;
  onChange: (id: string) => void;
  placeholder?: string;
  label?: string;
}

/** Backwards-compatible wrapper — existing callers pass `things` prop. */
export function ThingPicker({ things, ...rest }: Omit<EntityPickerProps<VosThing>, 'items'> & { things: VosThing[] }) {
  return <EntityPicker items={things} {...rest} />;
}

/** Generic searchable dropdown picker for any entity with Id and Name. */
export function EntityPicker<T extends PickerItem>({ items, value, onChange, placeholder = 'Search...', label }: EntityPickerProps<T>) {
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  const selected = items.find((t) => t.Id === value);

  const MAX_DISPLAY = 50;

  const { filtered, hasMore } = useMemo(() => {
    if (!query) {
      return { filtered: items.slice(0, MAX_DISPLAY), hasMore: items.length > MAX_DISPLAY };
    }
    const q = query.toLowerCase();
    const matches = items.filter((t) => t.Name.toLowerCase().includes(q));
    // Sort: exact match first, then starts-with, then contains (shorter names first within each group)
    matches.sort((a, b) => {
      const an = a.Name.toLowerCase();
      const bn = b.Name.toLowerCase();
      const aExact = an === q;
      const bExact = bn === q;
      if (aExact !== bExact) return aExact ? -1 : 1;
      const aStarts = an.startsWith(q);
      const bStarts = bn.startsWith(q);
      if (aStarts !== bStarts) return aStarts ? -1 : 1;
      return an.length - bn.length;
    });
    return { filtered: matches.slice(0, MAX_DISPLAY), hasMore: matches.length > MAX_DISPLAY };
  }, [items, query]);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  return (
    <div ref={ref} className="relative">
      {label && <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{label}</label>}
      <input
        type="text"
        value={open ? query : selected?.Name || query}
        onChange={(e) => {
          setQuery(e.target.value);
          setOpen(true);
        }}
        onFocus={() => setOpen(true)}
        placeholder={placeholder}
        className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
      />
      {open && filtered.length > 0 && (
        <div className="absolute z-10 mt-1 w-full max-h-48 overflow-auto rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 shadow-lg">
          {filtered.map((t) => (
            <button
              key={t.Id}
              type="button"
              onClick={() => {
                onChange(t.Id);
                setQuery(t.Name);
                setOpen(false);
              }}
              className="w-full text-left px-3 py-1.5 text-sm hover:bg-zinc-100 dark:hover:bg-zinc-700 text-zinc-900 dark:text-zinc-100"
            >
              {t.Name}
              <span className="ml-2 text-xs text-zinc-400">{t.Id.substring(0, 8)}</span>
            </button>
          ))}
          {hasMore && (
            <div className="px-3 py-1.5 text-xs text-zinc-400 dark:text-zinc-500 text-center border-t border-zinc-200 dark:border-zinc-700">
              Type to search more items...
            </div>
          )}
        </div>
      )}
    </div>
  );
}
