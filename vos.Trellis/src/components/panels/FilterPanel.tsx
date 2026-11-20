import { useState, type ReactNode } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { numberIn } from '../../i18n/numbers';
import { TABLET_WIDTH, useIsWide } from '../../hooks/useIsWide';
import { FilterControls, type FilterControlsProperties } from './FilterControls';

export function FilterPanel({ title, shown, total, controls, noMatch, beforeList, children }: {
  title: string;
  shown: number;
  total: number;
  controls: FilterControlsProperties;
  noMatch: string | null;
  beforeList?: ReactNode;
  children: ReactNode;
}) {
  const wide = useIsWide(TABLET_WIDTH);
  const [collapsed, setCollapsed] = useState(!wide);

  return (
    <div className={`border border-zinc-700/60 rounded-lg bg-zinc-800/60 backdrop-blur text-zinc-200 text-xs overflow-hidden flex flex-col ${collapsed ? 'flex-shrink-0' : 'flex-1 min-h-0'}`}>
      <button
        type="button"
        onClick={() => setCollapsed((c) => !c)}
        className="w-full flex items-center justify-between px-3 py-2 hover:bg-zinc-700/40 transition-colors flex-shrink-0"
        aria-expanded={!collapsed}
      >
        <div className="flex items-center gap-2 font-semibold">
          {collapsed ? <ChevronRight size={14} /> : <ChevronDown size={14} />}
          <span>{title}</span>
        </div>
        <span className="text-zinc-400 font-mono">
          {numberIn(shown)}/{numberIn(total)}
        </span>
      </button>

      {!collapsed && (
        <div className="border-t border-zinc-700/60 flex flex-col min-h-0 flex-1">
          <FilterControls {...controls} />
          {beforeList}
          <ul className="flex-1 min-h-0 overflow-y-auto border-t border-zinc-700/60 divide-y divide-zinc-700/40">
            {noMatch !== null ? <li className="px-3 py-2 text-zinc-500 italic">{noMatch}</li> : children}
          </ul>
        </div>
      )}
    </div>
  );
}

export function FilterRow({ checked, indeterminate = false, onToggle, swatch, name, count }: {
  checked: boolean;
  indeterminate?: boolean;
  onToggle: () => void;
  swatch?: string;
  name: string;
  count: number;
}) {
  return (
    <li>
      <label className="flex items-center justify-between gap-2 px-3 py-1.5 hover:bg-zinc-700/30 cursor-pointer">
        <div className="flex items-center gap-2 flex-1 min-w-0">
          <input
            type="checkbox"
            checked={checked}
            ref={(element) => { if (element) element.indeterminate = indeterminate; }}
            onChange={onToggle}
            className="accent-violet-500"
          />
          {swatch !== undefined && (
            <span className="inline-block w-2 h-2 rounded-full flex-shrink-0" style={{ backgroundColor: swatch }} aria-hidden="true" />
          )}
          <span className="truncate text-zinc-200">{name}</span>
        </div>
        <span className="text-[10px] font-mono text-zinc-400">{numberIn(count)}</span>
      </label>
    </li>
  );
}
