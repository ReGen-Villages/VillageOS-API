import { useCallback, useEffect, useRef, useState } from 'react';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import type { OrbitControls as OrbitControlsImpl } from 'three-stdlib';
import * as THREE from 'three';
import { FragmentsModels, type FragmentsModel } from '@thatopen/fragments';
import { LoadingOverlay } from './LoadingOverlay';
import { ViewerToolbar, type CameraMode } from './ViewerToolbar';
import { orbitMouseButtonsFor } from '../../utils/orbitMouseButtons';

/** Map of IFC GlobalId → VosThing GUID, served by Mycelium at /api/model/mapping. */
export type FragmentsMapping = Record<string, string>;

interface ModelBounds {
  center: THREE.Vector3;
  size: THREE.Vector3;
  minY: number;
  maxY: number;
  maxDim: number;
}

type LoadState =
  | { kind: 'loading'; stage: string; progress: number }
  | { kind: 'ready'; bounds: ModelBounds };

interface FragmentsViewerProps {
  fragmentsBytes: ArrayBuffer;
  mapping: FragmentsMapping;
  /** Fires with the VosThing GUID of the clicked element, or null when the click missed geometry. */
  onPick: (vosGuid: string | null) => void;
  /** IFC GlobalIds whose Fragments instances should be hidden. Empty → everything visible. */
  hiddenIfcGuids?: readonly string[];
}

export function FragmentsViewer({ fragmentsBytes, mapping, onPick, hiddenIfcGuids = [] }: FragmentsViewerProps) {
  const orbitRef = useRef<OrbitControlsImpl | null>(null);
  const [loadState, setLoadState] = useState<LoadState>({ kind: 'loading', stage: 'fetching worker', progress: 0 });
  const [cameraMode, setCameraMode] = useState<CameraMode>('3d');
  const [sectionEnabled, setSectionEnabled] = useState(false);
  const [sectionY, setSectionY] = useState<number>(0);

  // Shift/Cmd/Ctrl + left-drag becomes pan (right-click is awkward on macOS trackpads).
  const [panModifier, setPanModifier] = useState({ shift: false, meta: false, ctrl: false });
  useEffect(() => {
    const sync = (e: KeyboardEvent) => {
      setPanModifier({ shift: e.shiftKey, meta: e.metaKey, ctrl: e.ctrlKey });
    };
    // Clear on blur so a key released while unfocused (e.g. Cmd+Tab) doesn't stick.
    const clear = () => setPanModifier({ shift: false, meta: false, ctrl: false });
    window.addEventListener('keydown', sync);
    window.addEventListener('keyup', sync);
    window.addEventListener('blur', clear);
    return () => {
      window.removeEventListener('keydown', sync);
      window.removeEventListener('keyup', sync);
      window.removeEventListener('blur', clear);
    };
  }, []);
  const mouseButtons = orbitMouseButtonsFor(panModifier);

  // Held in a ref because the Fragments worker reads it every frame via
  // getClippingPlanesEvent and must see current state without re-subscribing.
  const clipPlanesRef = useRef<THREE.Plane[]>([]);
  useEffect(() => {
    if (sectionEnabled) {
      const p = new THREE.Plane(new THREE.Vector3(0, -1, 0), sectionY);
      clipPlanesRef.current = [p];
    } else {
      clipPlanesRef.current = [];
    }
  }, [sectionEnabled, sectionY]);

  const onProgress = useCallback((stage: string, progress: number) => {
    setLoadState((prev) => (prev.kind === 'ready' ? prev : { kind: 'loading', stage, progress }));
  }, []);

  const onReady = useCallback((bounds: ModelBounds) => {
    setLoadState({ kind: 'ready', bounds });
    setSectionY(bounds.maxY);
  }, []);

  const bounds = loadState.kind === 'ready' ? loadState.bounds : null;

  return (
    <div className="relative w-full h-full" data-testid="fragments-viewer">
      <Canvas
        className="w-full h-full"
        data-testid="fragments-canvas"
        camera={{ position: [15, 15, 15], fov: 45, near: 0.1, far: 10000 }}
      >
        <color attach="background" args={['#0f0f10']} />
        <ambientLight intensity={0.6} />
        <directionalLight position={[30, 50, 20]} intensity={0.8} castShadow />

        {/* Bug #5298: swapping between two drei cameras broke FragmentsModel.raycast
            (returned null for every click). Use the single built-in camera; plan mode
            is faked by lifting it overhead and narrowing FOV instead of an ortho camera. */}
        <OrbitControls
          ref={orbitRef}
          makeDefault
          enableDamping
          dampingFactor={0.08}
          enableRotate={cameraMode === '3d'}
          enablePan
          mouseButtons={mouseButtons}
        />

        <FragmentsScene
          bytes={fragmentsBytes}
          orbitRef={orbitRef}
          mapping={mapping}
          onPick={onPick}
          onProgress={onProgress}
          onReady={onReady}
          cameraMode={cameraMode}
          clipPlanesRef={clipPlanesRef}
          hiddenIfcGuids={hiddenIfcGuids}
        />
      </Canvas>

      {loadState.kind === 'loading' && <LoadingOverlay stage={loadState.stage} progress={loadState.progress} />}

      {bounds && (
        <ViewerToolbar
          cameraMode={cameraMode}
          onCameraModeChange={setCameraMode}
          sectionEnabled={sectionEnabled}
          onSectionEnabledChange={setSectionEnabled}
          sectionY={sectionY}
          onSectionYChange={setSectionY}
          minY={bounds.minY}
          maxY={bounds.maxY}
        />
      )}
    </div>
  );
}

interface FragmentsSceneProps {
  bytes: ArrayBuffer;
  orbitRef: React.MutableRefObject<OrbitControlsImpl | null>;
  mapping: FragmentsMapping;
  onPick: (vosGuid: string | null) => void;
  onProgress: (stage: string, progress: number) => void;
  onReady: (bounds: ModelBounds) => void;
  cameraMode: CameraMode;
  clipPlanesRef: React.MutableRefObject<THREE.Plane[]>;
  hiddenIfcGuids: readonly string[];
}

const CLICK_MAX_DRAG_PX = 4;

const HIGHLIGHT_MATERIAL = {
  color: new THREE.Color('#fde047'),
  renderedFaces: 1,
  opacity: 1,
  transparent: false,
};

function FragmentsScene({
  bytes,
  orbitRef,
  mapping,
  onPick,
  onProgress,
  onReady,
  cameraMode,
  clipPlanesRef,
  hiddenIfcGuids,
}: FragmentsSceneProps) {
  const { camera, gl, invalidate } = useThree();
  const [model, setModel] = useState<FragmentsModel | null>(null);
  const fragmentsRef = useRef<FragmentsModels | null>(null);
  const boundsRef = useRef<ModelBounds | null>(null);
  const lastUpdateRef = useRef<number>(0);
  const highlightedRef = useRef<number | null>(null);

  // Local clipping must be enabled for the section plane to take effect.
  useEffect(() => {
    const prev = gl.localClippingEnabled;
    gl.localClippingEnabled = true;
    return () => {
      gl.localClippingEnabled = prev;
    };
  }, [gl]);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      onProgress('fetching worker', 0.05);
      const workerURL = await FragmentsModels.getWorker();
      if (cancelled) return;

      const fragments = new FragmentsModels(workerURL);
      fragmentsRef.current = fragments;

      const loaded = await fragments.load(bytes, {
        modelId: 'village-os-model',
        camera: camera as THREE.PerspectiveCamera,
        onProgress: ({ stage, progress }) => {
          if (cancelled) return;
          onProgress(stage, progress);
        },
      });
      if (cancelled) {
        await fragments.dispose();
        return;
      }
      loaded.getClippingPlanesEvent = () => clipPlanesRef.current;

      const bounds = await computeBounds(loaded);
      boundsRef.current = bounds;
      await fitCameraToBounds(cameraMode, camera, bounds, orbitRef.current);
      await fragments.update(true);
      invalidate();
      setModel(loaded);
      onReady(bounds);
    })().catch((err) => {
      if (!cancelled) console.error('Failed to load Fragments model', err);
    });

    return () => {
      cancelled = true;
      const fragments = fragmentsRef.current;
      fragmentsRef.current = null;
      if (fragments) void fragments.dispose();
    };
    // Load once per bytes instance.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bytes]);

  useEffect(() => {
    if (!model || !boundsRef.current) return;
    void fitCameraToBounds(cameraMode, camera, boundsRef.current, orbitRef.current);
    invalidate();
  }, [cameraMode, camera, model, orbitRef, invalidate]);

  useFrame(({ clock }) => {
    const fragments = fragmentsRef.current;
    if (!fragments || !model) return;
    const now = clock.elapsedTime;
    if (now - lastUpdateRef.current < 1 / 6) return;
    lastUpdateRef.current = now;
    void fragments.update();
  });

  // resetVisible() first so a removed entry comes back into view (cheaper than
  // diffing the previous hidden set).
  useEffect(() => {
    if (!model) return;
    let cancelled = false;
    (async () => {
      await model.resetVisible();
      if (cancelled) { invalidate(); return; }
      if (hiddenIfcGuids.length > 0) {
        const localIds = (await model.getLocalIdsByGuids([...hiddenIfcGuids]))
          .filter((id): id is number => typeof id === 'number');
        if (cancelled) { invalidate(); return; }
        if (localIds.length > 0) await model.setVisible(localIds, false);
      }
      const fragments = fragmentsRef.current;
      if (fragments && !cancelled) await fragments.update(true);
      invalidate();
    })().catch((err) => console.error('Failed to apply type-filter visibility', err));
    return () => { cancelled = true; };
  }, [model, hiddenIfcGuids, invalidate]);

  useEffect(() => {
    if (!model) return;
    const canvas = gl.domElement;
    let downX = 0;
    let downY = 0;

    const onPointerDown = (e: PointerEvent) => {
      downX = e.clientX;
      downY = e.clientY;
    };

    const onPointerUp = async (e: PointerEvent) => {
      const dx = Math.abs(e.clientX - downX);
      const dy = Math.abs(e.clientY - downY);
      if (dx > CLICK_MAX_DRAG_PX || dy > CLICK_MAX_DRAG_PX) return;
      if (e.button !== 0) return;

      // Bug #5298: Fragments' raycaster does the NDC conversion itself, so pass
      // raw client pixels — pre-normalised NDC makes it miss all geometry.
      const mouse = new THREE.Vector2(e.clientX, e.clientY);

      let hit;
      try {
        hit = await model.raycast({
          camera: camera as THREE.PerspectiveCamera,
          mouse,
          dom: canvas,
        });
      } catch (err) {
        console.error('raycast failed', err);
        return;
      }

      if (!hit) {
        await clearHighlight();
        onPick(null);
        return;
      }

      await applyHighlight(hit.localId);

      let ifcGuid: string | null = null;
      try {
        const [g] = await model.getGuidsByLocalIds([hit.localId]);
        ifcGuid = g ?? null;
      } catch {
        ifcGuid = null;
      }

      const vosGuid = ifcGuid ? (mapping[ifcGuid] ?? null) : null;
      onPick(vosGuid);
      invalidate();
    };

    const applyHighlight = async (localId: number) => {
      if (highlightedRef.current === localId) return;
      // Note: model.highlight(undefined, ...) highlights EVERY item (Bug
      // #5298 follow-up — the whole model turned yellow on the 2nd pick).
      // Use resetHighlight() to clear the previous selection instead.
      if (highlightedRef.current != null) {
        await model.resetHighlight([highlightedRef.current]);
      }
      await model.highlight([localId], HIGHLIGHT_MATERIAL);
      highlightedRef.current = localId;
    };

    const clearHighlight = async () => {
      if (highlightedRef.current == null) return;
      await model.resetHighlight([highlightedRef.current]);
      highlightedRef.current = null;
    };

    canvas.addEventListener('pointerdown', onPointerDown);
    canvas.addEventListener('pointerup', onPointerUp);
    return () => {
      canvas.removeEventListener('pointerdown', onPointerDown);
      canvas.removeEventListener('pointerup', onPointerUp);
    };
  }, [model, camera, gl, mapping, onPick, invalidate]);

  if (!model) return null;
  return <primitive object={model.object} />;
}

// ── Camera framing + bounds ─────────────────────────────────────────────────

async function computeBounds(model: FragmentsModel): Promise<ModelBounds> {
  const boxes = await model.getBoxes();
  const total = boxes.reduce((acc, b) => acc.union(b), new THREE.Box3().makeEmpty());
  const center = new THREE.Vector3();
  const size = new THREE.Vector3();
  total.getCenter(center);
  total.getSize(size);
  const maxDim = Math.max(size.x, size.y, size.z) || 1;
  return {
    center,
    size,
    minY: total.min.y,
    maxY: total.max.y,
    maxDim,
  };
}

async function fitCameraToBounds(
  mode: CameraMode,
  camera: THREE.Camera,
  bounds: ModelBounds,
  controls: OrbitControlsImpl | null,
): Promise<void> {
  if (!(camera instanceof THREE.PerspectiveCamera)) return;

  const { center, size } = bounds;

  if (mode === '3d') {
    // Diagonal 3/4 overhead view framing the full model.
    camera.fov = 45;
    const horizontal = Math.max(size.x, size.z, size.y) || 1;
    const distance = (horizontal / (2 * Math.tan((camera.fov * Math.PI) / 360))) * 1.2;
    const offset = new THREE.Vector3(distance * 0.8, distance * 0.6, distance * 0.8);
    camera.position.copy(center).add(offset);
    camera.near = Math.max(distance / 1000, 0.01);
    camera.far = distance * 1000;
  } else {
    // Plan mode: fake orthographic by lifting the same perspective camera
    // straight above the model and narrowing its FOV. Using a single camera
    // (rather than drei's OrthographicCamera) preserves Fragments picking
    // (Bug #5298).
    const horizontal = Math.max(size.x, size.z) || 1;
    const planFov = 10;
    const distance = horizontal / (2 * Math.tan((planFov * Math.PI) / 360)) * 1.1;
    camera.fov = planFov;
    camera.position.set(center.x, bounds.maxY + distance, center.z);
    camera.near = Math.max(distance / 1000, 0.01);
    camera.far = distance * 1000;
  }

  camera.updateProjectionMatrix();
  camera.lookAt(center);

  if (controls) {
    controls.target.copy(center);
    controls.update();
  }
}

