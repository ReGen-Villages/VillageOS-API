import { Search, X, Plus, Loader2 } from 'lucide-react';

interface Props {
  searchQuery: string;
  setSearchQuery: (q: string) => void;
  caseSensitive: boolean;
  setCaseSensitive: (fn: (prev: boolean) => boolean) => void;
  exactMatch: boolean;
  setExactMatch: (fn: (prev: boolean) => boolean) => void;
  useRegex: boolean;
  setUseRegex: (fn: (prev: boolean) => boolean) => void;
  matchCount: number;
  showCreateThing: boolean;
  setShowCreateThing: (fn: (prev: boolean) => boolean) => void;
  newThingName: string;
  setNewThingName: (name: string) => void;
  creatingThing: boolean;
  onCreateThing: () => void;
}

export function GraphSearchBar({
  searchQuery, setSearchQuery,
  caseSensitive, setCaseSensitive,
  exactMatch, setExactMatch,
  useRegex, setUseRegex,
  matchCount,
  showCreateThing, setShowCreateThing,
  newThingName, setNewThingName,
  creatingThing, onCreateThing,
}: Props) {
  return (
    <>
      <div className="absolute top-3 left-3 z-10 flex items-center gap-2 bg-zinc-800/80 backdrop-blur rounded-lg px-3 py-1.5">
        <Search size={14} className="text-zinc-400 flex-shrink-0" />
        <input
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          placeholder={useRegex ? 'Regex pattern...' : 'Search things (comma = list)...'}
          className="bg-transparent text-sm text-zinc-200 placeholder-zinc-500 focus:outline-none w-56"
        />
        <button
          onClick={() => setCaseSensitive((v) => !v)}
          title="Match case"
          className={`px-1 py-0.5 text-xs font-semibold rounded transition-colors flex-shrink-0 ${
            caseSensitive
              ? 'bg-blue-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          Aa
        </button>
        <button
          onClick={() => setExactMatch((v) => !v)}
          title="Exact match"
          className={`px-1 py-0.5 text-xs font-semibold rounded transition-colors flex-shrink-0 ${
            exactMatch
              ? 'bg-blue-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          =
        </button>
        <button
          onClick={() => setUseRegex((v) => !v)}
          title="Regular expression"
          className={`px-1 py-0.5 text-xs font-semibold rounded transition-colors flex-shrink-0 ${
            useRegex
              ? 'bg-blue-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          .*
        </button>
        {searchQuery && (
          <>
            <button
              onClick={() => setSearchQuery('')}
              title="Clear search"
              className="text-zinc-500 hover:text-zinc-300 transition-colors flex-shrink-0"
            >
              <X size={14} />
            </button>
            <span className="text-xs text-zinc-500 flex-shrink-0">
              {matchCount} found
            </span>
          </>
        )}
        <div className="w-px h-4 bg-zinc-600 flex-shrink-0" />
        <button
          onClick={() => setShowCreateThing((v) => !v)}
          title="Create thing"
          className={`p-0.5 rounded transition-colors flex-shrink-0 ${
            showCreateThing
              ? 'bg-emerald-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          <Plus size={14} />
        </button>
      </div>
      {showCreateThing && (
        <div className="absolute top-12 left-3 z-10 flex items-center gap-2 bg-zinc-800/90 backdrop-blur rounded-lg px-3 py-1.5">
          <input
            autoFocus
            value={newThingName}
            onChange={(e) => setNewThingName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') onCreateThing();
              if (e.key === 'Escape') { setShowCreateThing(() => false); setNewThingName(''); }
            }}
            placeholder="New thing name..."
            disabled={creatingThing}
            className="bg-transparent text-sm text-zinc-200 placeholder-zinc-500 focus:outline-none w-48"
          />
          <button
            onClick={onCreateThing}
            disabled={!newThingName.trim() || creatingThing}
            className="p-0.5 rounded text-emerald-400 hover:bg-emerald-500/20 disabled:opacity-30 transition-colors flex-shrink-0"
            title="Create"
          >
            {creatingThing ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />}
          </button>
          <button
            onClick={() => { setShowCreateThing(() => false); setNewThingName(''); }}
            className="text-zinc-500 hover:text-zinc-300 transition-colors flex-shrink-0"
            title="Cancel"
          >
            <X size={14} />
          </button>
        </div>
      )}
    </>
  );
}
