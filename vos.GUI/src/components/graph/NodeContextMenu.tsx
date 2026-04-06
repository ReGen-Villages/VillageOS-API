import { useEffect, useCallback, useRef } from 'react';
import { Info, Expand, Unplug, Box, Copy, Trash2 } from 'lucide-react';
import { useUiStore } from '../../stores/uiStore';
import { toast } from '../common/Toast';
import { canSupport3D } from '../../utils/browserDetect';
import type { VosThing, VosRelationship } from '../../types/vos';

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
  onDeleteThing: (id: string, name: string) => void;
}

/**
 * Dropdown context menu shown on right-click of a graph node.
 *
 * Actions are context-aware: some items only appear when relevant
 * (e.g. "View in 3D" requires geometry + WebGL support).
 *
 * Rendered as a sibling to SigmaCanvas (outside SigmaContainer),
 * absolutely positioned within the graph page's relative container.
 */
export function NodeContextMenu({ things, relationships, onDeleteThing }: Props) {
  const open = useUiStore((s) => s.nodeContextMenuOpen);
  const position = useUiStore((s) => s.nodeContextMenuPosition);
  const nodeId = useUiStore((s) => s.nodeContextMenuNodeId);
  const closeMenu = useUiStore((s) => s.closeNodeContextMenu);
  const selectNode = useUiStore((s) => s.selectNode);
  const toggleNodeExpanded = useUiStore((s) => s.toggleNodeExpanded);
  const toggleLogicalExpansion = useUiStore((s) => s.toggleLogicalExpansion);
  const mapEnabled = useUiStore((s) => s.mapEnabled);
  const menuRef = useRef<HTMLDivElement>(null);

  // Close on Escape
  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeMenu();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [open, closeMenu]);

  // Close on click outside
  useEffect(() => {
    if (!open) return;
    const handleClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        closeMenu();
      }
    };
    const timer = setTimeout(() => {
      window.addEventListener('mousedown', handleClick);
    }, 100);
    return () => {
      clearTimeout(timer);
      window.removeEventListener('mousedown', handleClick);
    };
  }, [open, closeMenu]);

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
        () => toast.success('Node ID copied'),
        () => toast.error('Failed to copy'),
      );
    }
    closeMenu();
  }, [nodeId, closeMenu]);

  const handleDelete = useCallback(() => {
    if (!nodeId) return;
    const thing = things.find((t) => t.Id === nodeId);
    const name = thing?.Name || nodeId;
    onDeleteThing(nodeId, name);
    closeMenu();
  }, [nodeId, things, onDeleteThing, closeMenu]);

  if (!open || !position || !nodeId) return null;

  // Determine which actions to show based on thing attributes
  const thing = things.find((t) => t.Id === nodeId);
  const hasGeometry = thing?.Properties?.geometry !== undefined;
  const hasLogicalChildren = hasGeometry && relationships.some(
    (r) => r.SubjectId === nodeId && !things.find((t) => t.Id === r.TargetId)?.Properties?.geometry,
  );
  const showLogicalToggle = hasGeometry && !mapEnabled && hasLogicalChildren;
  const showView3D = hasGeometry && canSupport3D();
  const nodeName = thing?.Name || nodeId;

  return (
    <div
      ref={menuRef}
      className="absolute z-50 min-w-[180px] py-1 bg-zinc-800/95 backdrop-blur border border-zinc-700 rounded-lg shadow-xl shadow-black/40"
      style={{ left: position.x, top: position.y }}
    >
      {/* Node name header */}
      <div className="px-3 py-1.5 text-xs text-zinc-400 font-medium truncate border-b border-zinc-700/50 max-w-[220px]">
        {nodeName}
      </div>

      <MenuItem icon={Info} label="View Details" onClick={handleViewDetails} />
      <MenuItem icon={Expand} label="Expand Relationships" onClick={handleExpandRelationships} />
      {showLogicalToggle && (
        <MenuItem icon={Unplug} label="Toggle Logical Nodes" onClick={handleToggleLogical} />
      )}
      {showView3D && (
        <MenuItem icon={Box} label="View in 3D" onClick={handleViewDetails} />
      )}
      <MenuItem icon={Copy} label="Copy ID" onClick={handleCopyId} />

      {/* Divider before destructive action */}
      <div className="my-1 border-t border-zinc-700/50" />
      <MenuItem icon={Trash2} label="Delete" onClick={handleDelete} danger />
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
