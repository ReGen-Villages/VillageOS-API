import { Search, X } from 'lucide-react';

/** The one search box a row list draws, whatever it sits on: a table widget's header or an opened
 *  figure. Clearing it is a press on the cross, which appears only while there is something to clear. */
export function SearchBox({
  value,
  onChange,
  placeholder,
  className = '',
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  className?: string;
}) {
  return (
    <div className={`flex items-center gap-1.5 rounded px-2 py-1 ${className}`}>
      <Search size={12} className="text-zinc-400 flex-shrink-0" />
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="bg-transparent text-xs text-zinc-700 dark:text-zinc-200 placeholder-zinc-400 dark:placeholder-zinc-600 focus:outline-none flex-1 min-w-0"
      />
      {value && (
        <button onClick={() => onChange('')} className="text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300 flex-shrink-0">
          <X size={12} />
        </button>
      )}
    </div>
  );
}
