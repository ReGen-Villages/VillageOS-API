import { useState } from 'react';
import { Plus } from 'lucide-react';

export function NewNameField({ label, addLabel, onName }: {
  label: string;
  addLabel: (name: string) => string;
  onName: (name: string) => void;
}) {
  const [name, setName] = useState('');

  const add = () => {
    const trimmed = name.trim();
    if (trimmed === '') return;
    setName('');
    onName(trimmed);
  };

  return (
    <div className="p-2 border-t border-zinc-200 dark:border-zinc-700 flex gap-1.5">
      <input
        value={name}
        onChange={(event) => setName(event.target.value)}
        onKeyDown={(event) => { if (event.key === 'Enter') add(); }}
        aria-label={label}
        placeholder={label}
        className="flex-1 min-w-0 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 text-sm text-zinc-900 dark:text-zinc-100"
      />
      <button
        type="button"
        onClick={add}
        aria-label={addLabel(name.trim())}
        className="rounded bg-zinc-100 dark:bg-zinc-800 px-2 text-zinc-700 dark:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-700"
      >
        <Plus size={15} />
      </button>
    </div>
  );
}
