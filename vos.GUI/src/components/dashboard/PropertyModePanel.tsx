import { useState, useEffect, useCallback } from 'react';
import { configApi } from '../../api/configApi';
import { toast } from '../common/Toast';
import type { PropertyModeConfig } from '../../types/vos';

const MODES = ['CurrentOnly', 'RingBuffer', 'Sampled', 'FullHistory'] as const;

export function PropertyModePanel() {
  const [config, setConfig] = useState<PropertyModeConfig | null>(null);
  const [mode, setMode] = useState('');
  const [ringBufferSize, setRingBufferSize] = useState('');
  const [sampleRate, setSampleRate] = useState('');
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await configApi.getDefaultPropertyMode();
      setConfig(data);
      setMode(data.Mode);
      setRingBufferSize(data.RingBufferSize?.toString() ?? '');
      setSampleRate(data.SampleRate?.toString() ?? '');
    } catch {
      // endpoint may not exist on older brokers
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  const save = async () => {
    setSaving(true);
    try {
      const data = await configApi.setDefaultPropertyMode(
        mode,
        ringBufferSize ? parseInt(ringBufferSize, 10) : undefined,
        sampleRate ? parseInt(sampleRate, 10) : undefined,
      );
      setConfig(data);
      toast.success('Property mode updated');
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to update property mode');
    } finally {
      setSaving(false);
    }
  };

  const dirty = config && (
    mode !== config.Mode ||
    (ringBufferSize || '') !== (config.RingBufferSize?.toString() ?? '') ||
    (sampleRate || '') !== (config.SampleRate?.toString() ?? '')
  );

  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <h3 className="text-sm font-semibold mb-3">Property Storage Mode</h3>
      {loading ? (
        <p className="text-xs text-zinc-500">Loading...</p>
      ) : !config ? (
        <p className="text-xs text-zinc-500 italic">Not available</p>
      ) : (
        <div className="space-y-3">
          <div>
            <label className="block text-xs font-medium text-zinc-500 mb-1">Default Mode</label>
            <select
              value={mode}
              onChange={(e) => setMode(e.target.value)}
              className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              {MODES.map((m) => <option key={m} value={m}>{m}</option>)}
            </select>
          </div>
          {mode === 'RingBuffer' && (
            <div>
              <label className="block text-xs font-medium text-zinc-500 mb-1">Ring Buffer Size</label>
              <input
                type="number"
                min={1}
                value={ringBufferSize}
                onChange={(e) => setRingBufferSize(e.target.value)}
                placeholder="e.g. 100"
                className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          )}
          {mode === 'Sampled' && (
            <div>
              <label className="block text-xs font-medium text-zinc-500 mb-1">Sample Rate</label>
              <input
                type="number"
                min={1}
                value={sampleRate}
                onChange={(e) => setSampleRate(e.target.value)}
                placeholder="e.g. 10"
                className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          )}
          <button
            onClick={save}
            disabled={saving || !dirty}
            className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {saving ? 'Saving...' : 'Apply'}
          </button>
        </div>
      )}
    </div>
  );
}
