import { useCallback } from 'react';
import { useSigma } from '@react-sigma/core';
import { ZoomIn, ZoomOut, Maximize, RefreshCw, Expand, Pause, Play, X, Layers, ScanSearch, Unplug } from 'lucide-react';
import { useUiStore } from '../../stores/uiStore';

/**
 * Graph toolbar with zoom, fit, re-layout, and clustering controls.
 * Must be rendered as a child of <SigmaContainer>.
 */
export function GraphToolbar() {
  const sigma = useSigma();
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const predicateStats = useUiStore((s) => s.predicateStats);
  const clearPredicateIds = useUiStore((s) => s.clearPredicateIds);
  const isLayoutFrozen = useUiStore((s) => s.isLayoutFrozen);
  const toggleLayoutFrozen = useUiStore((s) => s.toggleLayoutFrozen);
  const semanticZoomEnabled = useUiStore((s) => s.semanticZoomEnabled);
  const setSemanticZoomEnabled = useUiStore((s) => s.setSemanticZoomEnabled);
  const expandedLogicalParents = useUiStore((s) => s.expandedLogicalParents);
  const clearLogicalExpansions = useUiStore((s) => s.clearLogicalExpansions);
  const isSpreadActive = useUiStore((s) => s.isSpreadActive);
  const toggleSpreadActive = useUiStore((s) => s.toggleSpreadActive);

  const activePredicates = predicateStats.filter((s) => activePredicateIds.has(s.predicateId));

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
    // Randomise positions slightly to force the layout to re-settle
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

  const handleOpenRadialMenu = useCallback(() => {
    // Open radial menu at viewport centre
    const container = sigma.getContainer();
    const rect = container.getBoundingClientRect();
    useUiStore.getState().openRadialMenu({
      x: rect.width / 2,
      y: rect.height / 2,
    });
  }, [sigma]);

  const handleClearClustering = useCallback(() => {
    clearPredicateIds();
  }, [clearPredicateIds]);

  return (
    <div className="absolute bottom-3 left-3 z-10 flex items-center gap-1">
      {/* Standard controls */}
      <div className="flex gap-1 bg-zinc-800/80 backdrop-blur rounded-lg p-1">
        <ToolButton icon={ZoomIn} label="Zoom in" onClick={handleZoomIn} />
        <ToolButton icon={ZoomOut} label="Zoom out" onClick={handleZoomOut} />
        <ToolButton icon={Maximize} label="Fit to viewport" onClick={handleFit} />
        <ToolButton icon={RefreshCw} label="Re-layout" onClick={handleRelayout} />
        <button
          onClick={toggleSpreadActive}
          disabled={isLayoutFrozen}
          title={isSpreadActive ? 'Disable spread mode' : 'Spread nodes apart'}
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
          title={isLayoutFrozen ? 'Resume layout' : 'Freeze layout'}
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
          title={semanticZoomEnabled ? 'Disable semantic zoom' : 'Enable semantic zoom'}
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
            title="Collapse all logical nodes"
            className="p-1.5 rounded hover:bg-zinc-700 text-zinc-300 hover:text-white transition-colors flex items-center gap-1"
          >
            <Unplug size={14} />
            <span className="text-[10px] font-mono text-zinc-400">{expandedLogicalParents.size}</span>
          </button>
        )}
      </div>

      {/* Cluster controls */}
      <div className="flex items-center gap-1 bg-zinc-800/80 backdrop-blur rounded-lg p-1">
        <ToolButton icon={Layers} label="Select predicates (right-click graph)" onClick={handleOpenRadialMenu} />
        {activePredicates.length > 0 && (
          <>
            <div className="flex items-center gap-1 px-1 py-1 text-xs text-zinc-300">
              {activePredicates.map((ap) => (
                <div key={ap.predicateId} className="flex items-center gap-1 px-1.5 py-0.5 rounded bg-zinc-700/50">
                  <div
                    className="w-2 h-2 rounded-full flex-shrink-0"
                    style={{ backgroundColor: ap.color }}
                  />
                  <span className="font-medium text-[10px]">{ap.predicateName}</span>
                  <span className="text-zinc-500 text-[9px]">{ap.edgeCount}</span>
                </div>
              ))}
            </div>
            <button
              onClick={handleClearClustering}
              title="Clear clustering"
              className="p-1 rounded hover:bg-zinc-700 text-zinc-500 hover:text-zinc-300 transition-colors"
            >
              <X size={14} />
            </button>
          </>
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
