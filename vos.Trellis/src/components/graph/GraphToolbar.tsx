import { useCallback } from 'react';
import { useSigma } from '@react-sigma/core';
import { useTranslation } from 'react-i18next';
import { ZoomIn, ZoomOut, Maximize, RefreshCw, Expand, Pause, Play, ScanSearch, Unplug } from 'lucide-react';
import { useUiStore } from '../../stores/uiStore';

/** Zoom/fit/re-layout/layout-mode controls. Child of <SigmaContainer>. */
export function GraphToolbar() {
  const { t } = useTranslation();
  const sigma = useSigma();
  const isLayoutFrozen = useUiStore((s) => s.isLayoutFrozen);
  const toggleLayoutFrozen = useUiStore((s) => s.toggleLayoutFrozen);
  const semanticZoomEnabled = useUiStore((s) => s.semanticZoomEnabled);
  const setSemanticZoomEnabled = useUiStore((s) => s.setSemanticZoomEnabled);
  const expandedLogicalParents = useUiStore((s) => s.expandedLogicalParents);
  const clearLogicalExpansions = useUiStore((s) => s.clearLogicalExpansions);
  const isSpreadActive = useUiStore((s) => s.isSpreadActive);
  const toggleSpreadActive = useUiStore((s) => s.toggleSpreadActive);

  const handleZoomIn = useCallback(() => {
    sigma.getCamera().animatedZoom({ duration: 200 });
  }, [sigma]);

  const handleZoomOut = useCallback(() => {
    sigma.getCamera().animatedUnzoom({ duration: 200 });
  }, [sigma]);

  const handleFit = useCallback(() => {
    sigma.getCamera().animatedReset({ duration: 300 });
  }, [sigma]);

  const handleRelayout = useCallback(() => {
    // Jitter positions to force the layout to re-settle.
    const graph = sigma.getGraph();
    graph.forEachNode((node) => {
      const attrs = graph.getNodeAttributes(node);
      if (!attrs.hasGeometry) {
        graph.setNodeAttribute(node, 'x', attrs.x + (Math.random() - 0.5) * 50);
        graph.setNodeAttribute(node, 'y', attrs.y + (Math.random() - 0.5) * 50);
      }
    });
    sigma.refresh();
  }, [sigma]);

  return (
    <div className="absolute bottom-3 left-3 z-10 flex items-center gap-1">
      <div className="flex gap-1 bg-zinc-800/80 backdrop-blur rounded-lg p-1">
        <ToolButton icon={ZoomIn} label={t('graph.toolbar.zoomIn')} onClick={handleZoomIn} />
        <ToolButton icon={ZoomOut} label={t('graph.toolbar.zoomOut')} onClick={handleZoomOut} />
        <ToolButton icon={Maximize} label={t('graph.toolbar.fit')} onClick={handleFit} />
        <ToolButton icon={RefreshCw} label={t('graph.toolbar.relayout')} onClick={handleRelayout} />
        <button
          onClick={toggleSpreadActive}
          disabled={isLayoutFrozen}
          title={isSpreadActive ? t('graph.toolbar.spreadOff') : t('graph.toolbar.spread')}
          className={`p-1.5 rounded transition-colors ${
            isLayoutFrozen
              ? 'text-zinc-600 cursor-not-allowed'
              : isSpreadActive
                ? 'bg-blue-600/30 text-blue-400 hover:bg-blue-600/40'
                : 'hover:bg-zinc-700 text-zinc-300 hover:text-white'
          }`}
        >
          <Expand size={16} />
        </button>
        <button
          onClick={toggleLayoutFrozen}
          title={isLayoutFrozen ? t('graph.toolbar.resume') : t('graph.toolbar.freeze')}
          className={`p-1.5 rounded transition-colors ${
            isLayoutFrozen
              ? 'bg-blue-600/30 text-blue-400 hover:bg-blue-600/40'
              : 'hover:bg-zinc-700 text-zinc-300 hover:text-white'
          }`}
        >
          {isLayoutFrozen ? <Play size={16} /> : <Pause size={16} />}
        </button>
        <button
          onClick={() => setSemanticZoomEnabled(!semanticZoomEnabled)}
          title={semanticZoomEnabled ? t('graph.toolbar.semanticOff') : t('graph.toolbar.semanticOn')}
          className={`p-1.5 rounded transition-colors ${
            semanticZoomEnabled
              ? 'bg-blue-600/30 text-blue-400 hover:bg-blue-600/40'
              : 'hover:bg-zinc-700 text-zinc-300 hover:text-white'
          }`}
        >
          <ScanSearch size={16} />
        </button>
        {expandedLogicalParents.size > 0 && (
          <button
            onClick={clearLogicalExpansions}
            title={t('graph.toolbar.collapseLogical')}
            className="p-1.5 rounded hover:bg-zinc-700 text-zinc-300 hover:text-white transition-colors flex items-center gap-1"
          >
            <Unplug size={14} />
            <span className="text-[10px] font-mono text-zinc-400">{expandedLogicalParents.size}</span>
          </button>
        )}
      </div>

    </div>
  );
}

function ToolButton({
  icon: Icon,
  label,
  onClick,
}: {
  icon: typeof ZoomIn;
  label: string;
  onClick: () => void;
}) {
  return (
    <button
      onClick={onClick}
      title={label}
      className="p-1.5 rounded hover:bg-zinc-700 text-zinc-300 hover:text-white transition-colors"
    >
      <Icon size={16} />
    </button>
  );
}
