import { Box, Layers, Scissors } from 'lucide-react';
import { useTranslation } from 'react-i18next';

export type CameraMode = '3d' | 'plan';

export interface ViewerToolbarProps {
  cameraMode: CameraMode;
  onCameraModeChange: (m: CameraMode) => void;
  sectionEnabled: boolean;
  onSectionEnabledChange: (v: boolean) => void;
  sectionY: number;
  onSectionYChange: (y: number) => void;
  minY: number;
  maxY: number;
}

export function ViewerToolbar({
  cameraMode,
  onCameraModeChange,
  sectionEnabled,
  onSectionEnabledChange,
  sectionY,
  onSectionYChange,
  minY,
  maxY,
}: ViewerToolbarProps) {
  const { t } = useTranslation();
  return (
    <div
      data-testid="fragments-toolbar"
      className="absolute top-3 right-3 flex flex-col gap-2 items-end"
    >
      <div className="flex rounded-md overflow-hidden border border-zinc-700 bg-zinc-900/90 backdrop-blur-sm">
        <ToolbarButton
          active={cameraMode === '3d'}
          onClick={() => onCameraModeChange('3d')}
          testid="camera-mode-3d"
          label="3D"
          icon={<Box size={14} />}
        />
        <ToolbarButton
          active={cameraMode === 'plan'}
          onClick={() => onCameraModeChange('plan')}
          testid="camera-mode-plan"
          label="Plan"
          icon={<Layers size={14} />}
        />
      </div>

      <div className="rounded-md border border-zinc-700 bg-zinc-900/90 backdrop-blur-sm px-3 py-2 flex items-center gap-3 min-w-64">
        <button
          onClick={() => onSectionEnabledChange(!sectionEnabled)}
          data-testid="section-toggle"
          aria-pressed={sectionEnabled}
          className={`flex items-center gap-1.5 text-xs font-medium ${
            sectionEnabled ? 'text-blue-400' : 'text-zinc-400 hover:text-zinc-200'
          }`}
        >
          <Scissors size={14} />
          Section
        </button>
        <input
          type="range"
          min={minY}
          max={maxY}
          step={(maxY - minY) / 200 || 0.01}
          value={sectionY}
          onChange={(e) => onSectionYChange(Number(e.target.value))}
          disabled={!sectionEnabled}
          data-testid="section-slider"
          aria-label={t('viewerToolbar.sectionHeight')}
          className="flex-1 accent-blue-500 disabled:opacity-40"
        />
      </div>
    </div>
  );
}

function ToolbarButton({
  active,
  onClick,
  testid,
  label,
  icon,
}: {
  active: boolean;
  onClick: () => void;
  testid: string;
  label: string;
  icon: React.ReactNode;
}) {
  return (
    <button
      onClick={onClick}
      data-testid={testid}
      aria-pressed={active}
      className={`flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium transition-colors ${
        active ? 'bg-blue-600 text-white' : 'text-zinc-300 hover:bg-zinc-800'
      }`}
    >
      {icon}
      {label}
    </button>
  );
}
