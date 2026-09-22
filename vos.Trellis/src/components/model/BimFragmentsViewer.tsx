import { useCallback, useEffect, useRef, useState } from 'react';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import type { OrbitControls as OrbitControlsImpl } from 'three-stdlib';
import * as THREE from 'three';
import { FragmentsModels as BimFragmentsModels, type FragmentsModel as BimFragmentsModel } from '@thatopen/fragments';
import { LoadingOverlay } from './LoadingOverlay';
import { ViewerToolbar, type CameraMode } from './ViewerToolbar';
import { orbitMouseButtonsFor } from '../../utils/orbitMouseButtons';
import { hiddenSceneItemsFor, type SceneVisibility } from './sceneVisibility';

/** Map of IFC GlobalId → VosThing GUID, served by Mycelium at /api/model/mapping. */
export type BimFragmentsMapping = Record<string, string>;

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

interface BimFragmentsViewerProps {
  bimFragmentsBytes: ArrayBuffer;
  mapping: BimFragmentsMapping;
  /** Fires with the VosThing GUID of the clicked element, or null when the click missed geometry. */
  onPick: (vosGuid: string | null) => void;
  /**
   * What the type filter says the scene should show. Required, and the caller
   * must hold it stable across renders — a fresh object every render re-runs
   * the whole visibility pass over the model.
   */
  visibility: SceneVisibility;
}

export function BimFragmentsViewer({
  bimFragmentsBytes,
  mapping,
  onPick,
  visibility,
}: BimFragmentsViewerProps) {
  const orbitReference = useRef<OrbitControlsImpl | null>(null);
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
  const clipPlanesReference = useRef<THREE.Plane[]>([]);
  useEffect(() => {
    if (sectionEnabled) {
      const p = new THREE.Plane(new THREE.Vector3(0, -1, 0), sectionY);
      clipPlanesReference.current = [p];
    } else {
      clipPlanesReference.current = [];
    }
  }, [sectionEnabled, sectionY]);

  const onProgress = useCallback((stage: string, progress: number) => {
    setLoadState((previous) => (previous.kind === 'ready' ? previous : { kind: 'loading', stage, progress }));
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

        {/* Swapping between two drei cameras broke BimFragmentsModel.raycast
            (returned null for every click). Use the single built-in camera; plan mode
            is faked by lifting it overhead and narrowing FOV instead of an ortho camera. */}
        <OrbitControls
          ref={orbitReference}
          makeDefault
          enableDamping
          dampingFactor={0.08}
          enableRotate={cameraMode === '3d'}
          enablePan
          mouseButtons={mouseButtons}
        />

        <BimFragmentsScene
          bytes={bimFragmentsBytes}
          orbitReference={orbitReference}
          mapping={mapping}
          onPick={onPick}
          onProgress={onProgress}
          onReady={onReady}
          cameraMode={cameraMode}
          clipPlanesReference={clipPlanesReference}
          visibility={visibility}
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

interface BimFragmentsSceneProps {
  bytes: ArrayBuffer;
  orbitReference: React.MutableRefObject<OrbitControlsImpl | null>;
  mapping: BimFragmentsMapping;
  onPick: (vosGuid: string | null) => void;
  onProgress: (stage: string, progress: number) => void;
  onReady: (bounds: ModelBounds) => void;
  cameraMode: CameraMode;
  clipPlanesReference: React.MutableRefObject<THREE.Plane[]>;
  visibility: SceneVisibility;
}

const CLICK_MAX_DRAG_PX = 4;

const HIGHLIGHT_MATERIAL = {
  color: new THREE.Color('#fde047'),
  renderedFaces: 1,
  opacity: 1,
  transparent: false,
};

function BimFragmentsScene({
  bytes,
  orbitReference,
  mapping,
  onPick,
  onProgress,
  onReady,
  cameraMode,
  clipPlanesReference,
  visibility,
}: BimFragmentsSceneProps) {
  const { camera, gl, invalidate } = useThree();
  const [model, setModel] = useState<BimFragmentsModel | null>(null);
  const bimFragmentsReference = useRef<BimFragmentsModels | null>(null);
  const boundsReference = useRef<ModelBounds | null>(null);
  const lastUpdateReference = useRef<number>(0);
  const highlightedReference = useRef<number | null>(null);

  // Local clipping must be enabled for the section plane to take effect.
  useEffect(() => {
    const previous = gl.localClippingEnabled;
    // Mutating the three-fiber renderer here is the intended Three.js API; the
    // immutability rule flags it only because `gl` comes from useThree().
    // eslint-disable-next-line react-hooks/immutability
    gl.localClippingEnabled = true;
    return () => {
      gl.localClippingEnabled = previous;
    };
  }, [gl]);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      onProgress('fetching worker', 0.05);
      const workerURL = await BimFragmentsModels.getWorker();
      if (cancelled) return;

      const bimFragments = new BimFragmentsModels(workerURL);
      bimFragmentsReference.current = bimFragments;

      const loaded = await bimFragments.load(bytes, {
        modelId: 'village-os-model',
        camera: camera as THREE.PerspectiveCamera,
        onProgress: ({ stage, progress }) => {
          if (cancelled) return;
          onProgress(stage, progress);
        },
      });
      if (cancelled) {
        await bimFragments.dispose();
        return;
      }
      loaded.getClippingPlanesEvent = () => clipPlanesReference.current;

      const bounds = await computeBounds(loaded);
      boundsReference.current = bounds;
      await fitCameraToBounds(cameraMode, camera, bounds, orbitReference.current);
      await bimFragments.update(true);
      invalidate();
      setModel(loaded);
      onReady(bounds);
    })().catch((err) => {
      if (!cancelled) console.error('Failed to load Fragments model', err);
    });

    return () => {
      cancelled = true;
      const bimFragments = bimFragmentsReference.current;
      bimFragmentsReference.current = null;
      if (bimFragments) void bimFragments.dispose();
    };
    // Load once per bytes instance.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bytes]);

  useEffect(() => {
    if (!model || !boundsReference.current) return;
    void fitCameraToBounds(cameraMode, camera, boundsReference.current, orbitReference.current);
    invalidate();
  }, [cameraMode, camera, model, orbitReference, invalidate]);

  useFrame(({ clock }) => {
    const bimFragments = bimFragmentsReference.current;
    if (!bimFragments || !model) return;
    const now = clock.elapsedTime;
    if (now - lastUpdateReference.current < 1 / 6) return;
    lastUpdateReference.current = now;
    void bimFragments.update();
  });

  // resetVisible() first so a removed entry comes back into view (cheaper than
  // diffing the previous hidden set).
  useEffect(() => {
    if (!model) return;
    let cancelled = false;
    (async () => {
      await model.resetVisible();
      if (cancelled) { invalidate(); return; }
      const toHide = await hiddenSceneItemsFor(visibility, model);
      if (cancelled) { invalidate(); return; }
      if (toHide.length > 0) await model.setVisible(toHide, false);
      const bimFragments = bimFragmentsReference.current;
      if (bimFragments && !cancelled) await bimFragments.update(true);
      invalidate();
    })().catch((err) => console.error('Failed to apply type-filter visibility', err));
    return () => { cancelled = true; };
  }, [model, visibility, invalidate]);

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

      // Fragments' raycaster does the NDC conversion itself, so pass
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
      if (highlightedReference.current === localId) return;
      // model.highlight(undefined, ...) highlights EVERY item — the whole model turned yellow on the
      // second pick — so resetHighlight() clears the previous selection instead.
      if (highlightedReference.current != null) {
        await model.resetHighlight([highlightedReference.current]);
      }
      await model.highlight([localId], HIGHLIGHT_MATERIAL);
      highlightedReference.current = localId;
    };

    const clearHighlight = async () => {
      if (highlightedReference.current == null) return;
      await model.resetHighlight([highlightedReference.current]);
      highlightedReference.current = null;
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

async function computeBounds(model: BimFragmentsModel): Promise<ModelBounds> {
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
    // (rather than drei's OrthographicCamera) preserves Fragments picking.
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
