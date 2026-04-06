/**
 * Full 3D village scene — renders all buildings with CityJSON Solid geometry.
 *
 * Uses @react-three/fiber (R3F) and @react-three/drei for camera controls.
 * Lazy-loaded via React.lazy() in GraphPage so Three.js is code-split.
 */
import { useMemo, useRef, useState, useCallback } from 'react';
import { Canvas, useFrame, type ThreeEvent } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import * as THREE from 'three';
import { X, RotateCcw, Maximize } from 'lucide-react';
import { prepareVillageScene, type VillageSceneData, type BuildingMesh as BuildingMeshData } from '../../utils/villageScene';
import { useUiStore } from '../../stores/uiStore';
import type { VosThing } from '../../types/vos';

// ── Types ────────────────────────────────────────────────────────────────

interface VillageScene3DProps {
  things: VosThing[];
  colorMap?: Map<string, string>;
  onExit: () => void;
}

// ── Building mesh component ─────────────────────────────────────────────

function Building({
  data,
  isSelected,
  isHovered,
  onPointerOver,
  onPointerOut,
  onClick,
}: {
  data: BuildingMeshData;
  isSelected: boolean;
  isHovered: boolean;
  onPointerOver: () => void;
  onPointerOut: () => void;
  onClick: (e: ThreeEvent<MouseEvent>) => void;
}) {
  const geometry = useMemo(() => {
    const geom = new THREE.BufferGeometry();
    geom.setAttribute('position', new THREE.BufferAttribute(data.mesh.positions, 3));
    geom.setAttribute('normal', new THREE.BufferAttribute(data.mesh.normals, 3));
    geom.setIndex(new THREE.BufferAttribute(data.mesh.indices, 1));
    return geom;
  }, [data.mesh]);

  const color = useMemo(() => new THREE.Color(data.color), [data.color]);

  const emissive = isSelected
    ? new THREE.Color('#2563eb')
    : isHovered
      ? new THREE.Color('#1e40af')
      : new THREE.Color('#000000');

  const emissiveIntensity = isSelected ? 0.4 : isHovered ? 0.25 : 0;

  return (
    <mesh
      geometry={geometry}
      onPointerOver={(e) => { e.stopPropagation(); onPointerOver(); }}
      onPointerOut={onPointerOut}
      onClick={onClick}
    >
      <meshStandardMaterial
        color={color}
        emissive={emissive}
        emissiveIntensity={emissiveIntensity}
        roughness={0.7}
        metalness={0.1}
        flatShading
      />
    </mesh>
  );
}

// ── Camera controller ───────────────────────────────────────────────────

function CameraController({
  targetPosition,
  bounds,
}: {
  targetPosition: THREE.Vector3 | null;
  bounds: VillageSceneData['bounds'];
}) {
  const controlsRef = useRef<any>(null);
  const targetRef = useRef(new THREE.Vector3());
  const posRef = useRef(new THREE.Vector3());
  const isAnimating = useRef(false);

  // Compute default overview position from bounds
  const overview = useMemo(() => {
    const cx = (bounds.minX + bounds.maxX) / 2;
    const cz = (bounds.minZ + bounds.maxZ) / 2;
    const dx = bounds.maxX - bounds.minX;
    const dz = bounds.maxZ - bounds.minZ;
    const diagonal = Math.sqrt(dx * dx + dz * dz);
    const height = Math.max(diagonal * 0.6, bounds.maxHeight * 8, 50);
    return {
      position: new THREE.Vector3(cx, height, cz + diagonal * 0.4),
      target: new THREE.Vector3(cx, 0, cz),
    };
  }, [bounds]);

  // Update target when selectedNode changes
  useMemo(() => {
    if (targetPosition) {
      targetRef.current.copy(targetPosition);
      posRef.current.set(
        targetPosition.x + 20,
        targetPosition.y + 15,
        targetPosition.z + 20,
      );
    } else {
      targetRef.current.copy(overview.target);
      posRef.current.copy(overview.position);
    }
    isAnimating.current = true;
  }, [targetPosition, overview]);

  useFrame(({ camera }) => {
    if (!isAnimating.current || !controlsRef.current) return;
    const controls = controlsRef.current;
    const speed = 0.04;

    camera.position.lerp(posRef.current, speed);
    controls.target.lerp(targetRef.current, speed);
    controls.update();

    if (camera.position.distanceTo(posRef.current) < 0.5) {
      isAnimating.current = false;
    }
  });

  return (
    <OrbitControls
      ref={controlsRef}
      target={overview.target.toArray() as [number, number, number]}
      maxPolarAngle={Math.PI / 2 - 0.05}
      minDistance={5}
      maxDistance={500}
      enableDamping
      dampingFactor={0.1}
    />
  );
}

// ── Ground plane ────────────────────────────────────────────────────────

function GroundPlane({ size }: { size: number }) {
  return (
    <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -0.01, 0]} receiveShadow>
      <planeGeometry args={[size, size]} />
      <meshStandardMaterial color="#0f0f14" roughness={1} />
    </mesh>
  );
}

// ── Scene toolbar (HTML overlay) ────────────────────────────────────────

function SceneToolbar({
  onExit,
  onResetCamera,
  onFitAll,
}: {
  onExit: () => void;
  onResetCamera: () => void;
  onFitAll: () => void;
}) {
  return (
    <div className="absolute bottom-3 left-3 z-10 flex items-center gap-1">
      <div className="flex gap-1 bg-zinc-800/80 backdrop-blur rounded-lg p-1">
        <button
          onClick={onFitAll}
          title="Fit all buildings"
          className="p-1.5 rounded hover:bg-zinc-700 text-zinc-300 hover:text-white transition-colors"
        >
          <Maximize size={16} />
        </button>
        <button
          onClick={onResetCamera}
          title="Reset camera"
          className="p-1.5 rounded hover:bg-zinc-700 text-zinc-300 hover:text-white transition-colors"
        >
          <RotateCcw size={16} />
        </button>
        <button
          onClick={onExit}
          title="Exit 3D view"
          className="p-1.5 rounded hover:bg-red-900/50 text-zinc-300 hover:text-red-400 transition-colors"
        >
          <X size={16} />
        </button>
      </div>
    </div>
  );
}

// ── Main scene component ────────────────────────────────────────────────

export default function VillageScene3D({ things, colorMap, onExit }: VillageScene3DProps) {
  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const selectNode = useUiStore((s) => s.selectNode);

  const [hoveredId, setHoveredId] = useState<string | null>(null);
  const [cameraKey, setCameraKey] = useState(0);

  const sceneData = useMemo(
    () => prepareVillageScene(things, colorMap),
    [things, colorMap],
  );

  const selectedPosition = useMemo(() => {
    if (!selectedNodeId || !sceneData) return null;
    const building = sceneData.buildings.find((b) => b.thingId === selectedNodeId);
    if (!building) return null;
    const [cx, cz] = building.mesh.localCenter;
    return new THREE.Vector3(cx, building.mesh.height / 2, cz);
  }, [selectedNodeId, sceneData]);

  const handleBuildingClick = useCallback((thingId: string, e: ThreeEvent<MouseEvent>) => {
    e.stopPropagation();
    selectNode(thingId);
  }, [selectNode]);

  const handleBackgroundClick = useCallback(() => {
    selectNode(null);
  }, [selectNode]);

  const handleResetCamera = useCallback(() => {
    selectNode(null);
    setCameraKey((k) => k + 1);
  }, [selectNode]);

  if (!sceneData) {
    return (
      <div className="h-full flex items-center justify-center text-zinc-500">
        No buildings with 3D geometry found.
      </div>
    );
  }

  const groundSize = Math.max(
    sceneData.bounds.maxX - sceneData.bounds.minX,
    sceneData.bounds.maxZ - sceneData.bounds.minZ,
  ) * 2;

  return (
    <div className="h-full relative">
      <Canvas
        shadows
        camera={{ fov: 50, near: 0.1, far: 5000 }}
        gl={{ antialias: true, alpha: false }}
        style={{ background: '#09090b' }}
        dpr={[1, 2]}
        onPointerMissed={handleBackgroundClick}
      >
        {/* Lighting */}
        <ambientLight color="#1e1e2e" intensity={0.5} />
        <directionalLight
          position={[100, 80, 60]}
          color="#e8d5b7"
          intensity={0.8}
          castShadow
          shadow-mapSize-width={2048}
          shadow-mapSize-height={2048}
        />
        <hemisphereLight
          color="#334155"
          groundColor="#0f0f14"
          intensity={0.3}
        />

        {/* Ground */}
        <GroundPlane size={groundSize} />

        {/* Buildings */}
        {sceneData.buildings.map((b) => (
          <Building
            key={b.thingId}
            data={b}
            isSelected={b.thingId === selectedNodeId}
            isHovered={b.thingId === hoveredId}
            onPointerOver={() => setHoveredId(b.thingId)}
            onPointerOut={() => setHoveredId(null)}
            onClick={(e) => handleBuildingClick(b.thingId, e)}
          />
        ))}

        {/* Camera */}
        <CameraController
          key={cameraKey}
          targetPosition={selectedPosition}
          bounds={sceneData.bounds}
        />
      </Canvas>

      <SceneToolbar
        onExit={onExit}
        onResetCamera={handleResetCamera}
        onFitAll={handleResetCamera}
      />

      {/* Building name tooltip */}
      {hoveredId && (
        <div className="absolute top-3 left-1/2 -translate-x-1/2 z-10 bg-zinc-800/90 backdrop-blur rounded-lg px-3 py-1.5 text-sm text-zinc-200 pointer-events-none">
          {sceneData.buildings.find((b) => b.thingId === hoveredId)?.thingName}
        </div>
      )}
    </div>
  );
}
