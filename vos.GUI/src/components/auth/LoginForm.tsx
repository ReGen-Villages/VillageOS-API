import { useState, useMemo, type FormEvent } from 'react';
import type { ModelSummary } from '../../types/vos';
import type { SeedStatus } from '../../api/brokerApi';
import { RegenLogo } from './RegenLogo';

interface LoginFormProps {
  onLogin: (username: string, password: string, modelId?: string) => Promise<void>;
  onSelectModel?: (modelId: string) => Promise<void>;
  onSaveSeed?: (name: string) => Promise<void>;
  error: string | null;
  loading: boolean;
  availableModels: ModelSummary[] | null;
  seedStatus?: SeedStatus | null;
}

type SortKey = 'name' | 'size';
type SortDir = 'asc' | 'desc';

function parseSeedInfo(name: string): { label: string; sizeMb: number | null } {
  // Name format from useAuth: "seedName  (12.3 MB)"
  const match = name.match(/^(.+?)\s{2}\(([0-9.]+)\s*MB\)$/);
  if (match) return { label: match[1], sizeMb: parseFloat(match[2]) };
  return { label: name, sizeMb: null };
}

export function LoginForm({ onLogin, onSelectModel, onSaveSeed, error, loading, availableModels, seedStatus }: LoginFormProps) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [selectedModelId, setSelectedModelId] = useState<string | null>(null);
  const [saveName, setSaveName] = useState('');
  const [search, setSearch] = useState('');
  const [sortKey, setSortKey] = useState<SortKey>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!username || !password) return;
    try {
      await onLogin(username, password, selectedModelId ?? undefined);
    } catch {
      // error is surfaced via the error prop
    }
  };

  const handleModelSelect = async (modelId: string) => {
    setSelectedModelId(modelId);
    try {
      if (!username && !password && onSelectModel) {
        await onSelectModel(modelId);
      } else {
        await onLogin(username, password, modelId);
      }
    } catch {
      // error is surfaced via the error prop
    }
  };

  const toggleSort = (key: SortKey) => {
    if (sortKey === key) {
      setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      setSortKey(key);
      setSortDir('asc');
    }
  };

  const sortArrow = (key: SortKey) => {
    if (sortKey !== key) return '⇅';
    return sortDir === 'asc' ? '↑' : '↓';
  };

  const filteredModels = useMemo(() => {
    if (!availableModels) return [];
    const q = search.toLowerCase();
    const filtered = q
      ? availableModels.filter(m => m.Name.toLowerCase().includes(q) || m.Id.toLowerCase().includes(q))
      : [...availableModels];

    filtered.sort((a, b) => {
      const infoA = parseSeedInfo(a.Name);
      const infoB = parseSeedInfo(b.Name);
      let cmp = 0;
      if (sortKey === 'name') {
        cmp = infoA.label.localeCompare(infoB.label);
      } else {
        cmp = (infoA.sizeMb ?? 0) - (infoB.sizeMb ?? 0);
      }
      return sortDir === 'asc' ? cmp : -cmp;
    });
    return filtered;
  }, [availableModels, search, sortKey, sortDir]);

  // Model/seed picker — shown after credentials validated OR during model switch
  if (availableModels && availableModels.length > 0 && (onSelectModel || (username && password))) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-900">
        <div className="w-full max-w-lg bg-gray-800 rounded-lg shadow-lg p-8">
          <div className="flex justify-center mb-2">
            <RegenLogo className="w-40 h-40" />
          </div>
          <p className="text-sm text-gray-400 text-center mb-4">Load a seed</p>
          {error && error !== 'Please select a model' && (
            <div className="bg-red-900/50 border border-red-500 text-red-200 px-3 py-2 rounded text-sm mb-4">
              {error}
            </div>
          )}

          {/* Search */}
          <div className="mb-3">
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search seeds..."
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:border-blue-500 text-sm"
            />
          </div>

          {/* Column headers */}
          <div className="flex items-center text-xs text-gray-400 px-3 py-1 mb-1 border-b border-gray-700">
            <button
              onClick={() => toggleSort('name')}
              className="flex-1 text-left hover:text-gray-200 transition-colors"
            >
              Name {sortArrow('name')}
            </button>
            <button
              onClick={() => toggleSort('size')}
              className="w-20 text-right hover:text-gray-200 transition-colors"
            >
              Size {sortArrow('size')}
            </button>
          </div>

          {/* Scrollable seed list */}
          <div className="max-h-80 overflow-y-auto space-y-1 pr-1">
            {filteredModels.length === 0 ? (
              <p className="text-sm text-gray-500 text-center py-4">
                {search ? 'No seeds match your search' : 'No seeds available'}
              </p>
            ) : (
              filteredModels.map((model) => {
                const { label, sizeMb } = parseSeedInfo(model.Name);
                return (
                  <button
                    key={model.Id}
                    onClick={() => handleModelSelect(model.Id)}
                    disabled={loading}
                    className="w-full flex items-center px-3 py-2 bg-gray-700 hover:bg-gray-600 disabled:bg-gray-600 disabled:cursor-not-allowed text-white text-left rounded transition-colors text-sm"
                  >
                    <span className="flex-1 font-medium truncate">{label}</span>
                    {sizeMb !== null && (
                      <span className="text-gray-400 text-xs ml-2 w-20 text-right shrink-0">
                        {sizeMb} MB
                      </span>
                    )}
                  </button>
                );
              })
            )}
          </div>

          {/* Result count */}
          <p className="text-xs text-gray-500 mt-2 px-1">
            {filteredModels.length} of {availableModels.length} seed{availableModels.length !== 1 ? 's' : ''}
          </p>

          {onSaveSeed && (
            <div className="mt-4 pt-4 border-t border-gray-700">
              <p className="text-sm text-gray-400 mb-2">Save current model</p>
              <form
                onSubmit={async (e) => {
                  e.preventDefault();
                  if (saveName.trim()) await onSaveSeed(saveName.trim());
                }}
                className="flex gap-2"
              >
                <input
                  type="text"
                  value={saveName}
                  onChange={(e) => setSaveName(e.target.value)}
                  placeholder="Seed name"
                  disabled={loading}
                  className="flex-1 px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:border-blue-500 text-sm"
                />
                <button
                  type="submit"
                  disabled={loading || !saveName.trim()}
                  className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 disabled:bg-gray-600 disabled:cursor-not-allowed text-white text-sm font-medium rounded transition-colors"
                >
                  Save
                </button>
              </form>
            </div>
          )}
        </div>
      </div>
    );
  }

  // Phase 1: Credentials
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-900">
      <div className="w-full max-w-sm bg-gray-800 rounded-lg shadow-lg p-8">
        <div className="flex justify-center mb-6">
          <RegenLogo className="w-40 h-40" />
        </div>
        <form onSubmit={handleSubmit} className="space-y-4">
          {error && error !== 'Please select a model' && (
            <div className="bg-red-900/50 border border-red-500 text-red-200 px-3 py-2 rounded text-sm">
              {error}
            </div>
          )}
          <div>
            <label htmlFor="username" className="block text-sm font-medium text-gray-300 mb-1">
              Username
            </label>
            <input
              id="username"
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:border-blue-500"
              placeholder="Enter username"
              autoComplete="username"
              autoFocus
              disabled={loading}
            />
          </div>
          <div>
            <label htmlFor="password" className="block text-sm font-medium text-gray-300 mb-1">
              Password
            </label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400 focus:outline-none focus:border-blue-500"
              placeholder="Enter password"
              autoComplete="current-password"
              disabled={loading}
            />
          </div>
          {seedStatus?.IsLoading && (
            <div className="bg-blue-900/40 border border-blue-700 text-blue-200 px-3 py-3 rounded text-sm space-y-1">
              <div className="flex items-center gap-2">
                <svg className="animate-spin h-4 w-4 text-blue-400" viewBox="0 0 24 24">
                  <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" fill="none" />
                  <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                </svg>
                <span className="font-medium">Loading seed: {seedStatus.CurrentFile}</span>
              </div>
              <div className="text-xs text-blue-300 pl-6">
                {seedStatus.Phase}
                {seedStatus.ThingsLoaded > 0 && ` — ${seedStatus.ThingsLoaded.toLocaleString()} things`}
                {seedStatus.RelationshipsLoaded > 0 && `, ${seedStatus.RelationshipsLoaded.toLocaleString()} relationships`}
              </div>
              <div className="text-xs text-blue-400 pl-6">
                Will sign in automatically when ready.
              </div>
            </div>
          )}
          <button
            type="submit"
            disabled={loading || !username || !password || !!seedStatus?.IsLoading}
            className="w-full py-2 px-4 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 disabled:cursor-not-allowed text-white font-medium rounded transition-colors"
          >
            {seedStatus?.IsLoading ? 'Waiting for seed...' : loading ? 'Signing in...' : 'Sign In'}
          </button>
        </form>
      </div>
    </div>
  );
}
