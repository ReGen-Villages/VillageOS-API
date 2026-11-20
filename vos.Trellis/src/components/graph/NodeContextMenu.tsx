import { useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useClosesOnEscapeOrPressOutside } from '../../hooks/useClosesOnEscapeOrPressOutside';
import { Info, Expand, Unplug, Box, Copy, Trash2 } from 'lucide-react';
import { useUiStore } from '../../stores/uiStore';
import { toast } from '../common/toastStore';
import { canSupport3D } from '../../utils/browserDetect';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
  onDeleteThing: (id: string, name: string) => void;
}

/** Rendered beside the Sigma canvas, outside its container, and positioned within the graph page. */
export function NodeContextMenu({ things, relationships, onDeleteThing }: Props) {
  const { t } = useTranslation();
  const open = useUiStore((s) => s.nodeContextMenuOpen);
  const position = useUiStore((s) => s.nodeContextMenuPosition);
  const nodeId = useUiStore((s) => s.nodeContextMenuNodeId);
  const closeMenu = useUiStore((s) => s.closeNodeContextMenu);
  const selectNode = useUiStore((s) => s.selectNode);
  const toggleNodeExpanded = useUiStore((s) => s.toggleNodeExpanded);
  const toggleLogicalExpansion = useUiStore((s) => s.toggleLogicalExpansion);
  const menuReference = useRef<HTMLDivElement>(null);
  useClosesOnEscapeOrPressOutside(open, menuReference, closeMenu);

  const handleViewDetails = useCallback(() => {
    if (nodeId) selectNode(nodeId);
    closeMenu();
  }, [nodeId, selectNode, closeMenu]);

  const handleExpandRelationships = useCallback(() => {
    if (nodeId) toggleNodeExpanded(nodeId);
    closeMenu();
  }, [nodeId, toggleNodeExpanded, closeMenu]);

  const handleToggleLogical = useCallback(() => {
    if (nodeId) toggleLogicalExpansion(nodeId);
    closeMenu();
  }, [nodeId, toggleLogicalExpansion, closeMenu]);

  const handleCopyId = useCallback(() => {
    if (nodeId) {
      navigator.clipboard.writeText(nodeId).then(
        () => toast.success(t('graph.toast.idCopied')),
        () => toast.error(t('graph.toast.copyFailed')),
      );
    }
    closeMenu();
  }, [nodeId, closeMenu, t]);

  const handleDelete = useCallback(() => {
    if (!nodeId) return;
    const thing = things.find((t) => t.Id === nodeId);
    const name = thing?.Name || nodeId;
    onDeleteThing(nodeId, name);
    closeMenu();
  }, [nodeId, things, onDeleteThing, closeMenu]);

  if (!open || !position || !nodeId) return null;

  const thing = things.find((t) => t.Id === nodeId);
  const hasGeometry = thing?.Properties?.geometry !== undefined;
  const hasLogicalChildren = hasGeometry && relationships.some(
    (r) => r.SubjectId === nodeId && !things.find((t) => t.Id === r.TargetId)?.Properties?.geometry,
  );
  const showLogicalToggle = hasGeometry && hasLogicalChildren;
  const showView3D = hasGeometry && canSupport3D();
  const nodeName = thing?.Name || nodeId;

  return (
    <div
      ref={menuReference}
      className="absolute z-50 min-w-[180px] py-1 bg-zinc-800/95 backdrop-blur border border-zinc-700 rounded-lg shadow-xl shadow-black/40"
      style={{ left: position.x, top: position.y }}
    >
      <div className="px-3 py-1.5 text-xs text-zinc-400 font-medium truncate border-b border-zinc-700/50 max-w-[220px]">
        {nodeName}
      </div>

      <MenuItem icon={Info} label={t('graph.contextMenu.viewDetails')} onClick={handleViewDetails} />
      <MenuItem icon={Expand} label={t('graph.contextMenu.expandRelationships')} onClick={handleExpandRelationships} />
      {showLogicalToggle && (
        <MenuItem icon={Unplug} label={t('graph.contextMenu.toggleLogical')} onClick={handleToggleLogical} />
      )}
      {showView3D && (
        <MenuItem icon={Box} label={t('graph.contextMenu.view3D')} onClick={handleViewDetails} />
      )}
      <MenuItem icon={Copy} label={t('graph.contextMenu.copyId')} onClick={handleCopyId} />

      <div className="my-1 border-t border-zinc-700/50" />
      <MenuItem icon={Trash2} label={t('graph.contextMenu.delete')} onClick={handleDelete} danger />
    </div>
  );
}

function MenuItem({
  icon: Icon,
  label,
  onClick,
  danger,
}: {
  icon: React.ComponentType<{ size?: number; className?: string }>;
  label: string;
  onClick: () => void;
  danger?: boolean;
}) {
  return (
    <button
      onClick={onClick}
      className={`w-full flex items-center gap-2.5 px-3 py-1.5 text-sm transition-colors
        ${
          danger
            ? 'text-red-400 hover:bg-red-500/15 hover:text-red-300'
            : 'text-zinc-300 hover:bg-zinc-700 hover:text-white'
        }`}
    >
      <Icon size={15} className={danger ? 'text-red-400' : 'text-zinc-500'} />
      {label}
    </button>
  );
}
