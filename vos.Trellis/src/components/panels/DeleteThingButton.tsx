import { Trash2 } from 'lucide-react';

/** Delete affordance for the node detail panel (#5828). Initiates deletion; the caller confirms
 * (GraphPage routes this through its delete-confirm modal) and performs the retract. */
export function DeleteThingButton({ onDelete }: { onDelete: () => void }) {
  return (
    <button
      onClick={onDelete}
      title="Delete (retract from model)"
      className="p-1 text-zinc-400 hover:text-red-400 transition-colors"
    >
      <Trash2 size={14} />
    </button>
  );
}
