import { Copy, Pencil } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { formatGuid } from '../../utils/formatters';
import { toast } from '../common/toastStore';

export function CopyIdButton({ id }: { id: string }) {
  const { t } = useTranslation();
  const copy = () => {
    navigator.clipboard.writeText(id);
    toast.information(t('panels.node.idCopied'));
  };
  return (
    <button onClick={copy} className="flex items-center gap-1 text-xs text-zinc-500 hover:text-zinc-300">
      <Copy size={10} /> {formatGuid(id)}
    </button>
  );
}

export function EditModeToggle({ editing, onToggle, title, disabled = false }: {
  editing: boolean;
  onToggle: () => void;
  title: string;
  disabled?: boolean;
}) {
  return (
    <button
      onClick={onToggle}
      disabled={disabled}
      className={`p-0.5 rounded transition-colors disabled:opacity-30 disabled:cursor-default ${
        editing
          ? 'text-blue-400 bg-blue-500/20 hover:bg-blue-500/30'
          : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
      }`}
      title={title}
    >
      <Pencil size={12} />
    </button>
  );
}
