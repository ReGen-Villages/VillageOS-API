import { useEffect, useRef, useState } from 'react';
import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import type { OrbitControls as OrbitControlsImpl } from 'three-stdlib';
import * as THREE from 'three';
import { FragmentsModels, type FragmentsModel } from '@thatopen/fragments';

/**
 * Map of IFC GlobalId → VosThing GUID, produced by vos.Tools.IfcIngest and
 * served by the broker at /api/model/mapping. Used to resolve a picked
 * Fragments element to its graph identity.
 */
export type FragmentsMapping = Record<string, string>;

interface FragmentsViewerProps {
  fragmentsBytes: ArrayBuffer;
  mapping: FragmentsMapping;
  /** Fires with the VosThing GUID of the clicked element, or null when the click missed geometry. */
  onPick: (vosGuid: string | null) => void;
}

/**
 * Loads a ThatOpen Fragments .frag artifact into a three.js scene and
 * exposes click-to-pick resolution against the mapping sidecar.
 *
 * Sub-tasks C + D of Feature #5248. Section cuts, 2D plan toggle, and
 * loading UX polish land in sub-task E.
 */
export function FragmentsViewer({ fragmentsBytes, mapping, onPick }: FragmentsViewerProps) {
  const orbitRef = useRef<OrbitControlsImpl | null>(null);

  return (
    <Canvas
      camera={{ position: [15, 15, 15], fov: 45, near: 0.1, far: 10000 }}
      className="w-full h-full"
      data-testid="fragments-canvas"
    >
      <color attach="background" args={['#0f0f10']} />
      <ambientLight intensity={0.6} />
      <directionalLight position={[30, 50, 20]} intensity={0.8} castShadow />
      <OrbitControls
        ref={orbitRef}
        makeDefault
        enableDamping
        dampingFactor={0.08}
      />
      <FragmentsScene bytes={fragmentsBytes} orbitRef={orbitRef} mapping={mapping} onPick={onPick} />
    </Canvas>
  );
}

interface FragmentsSceneProps {
  bytes: ArrayBuffer;
  orbitRef: React.MutableRefObject<OrbitControlsImpl | null>;
  mapping: FragmentsMapping;
  onPick: (vosGuid: string | null) => void;
}

const CLICK_MAX_DRAG_PX = 4;

const HIGHLIGHT_MATERIAL = {
  color: new THREE.Color('#fde047'), // tailwind yellow-300
  renderedFaces: 1,
  opacity: 1,
  transparent: false,
};

/**
 * Loads the Fragments model into the r3f scene. Runs inside <Canvas> so
 * useThree is available.
 */
function FragmentsScene({ bytes, orbitRef, mapping, onPick }: FragmentsSceneProps) {
  const { camera, gl, invalidate } = useThree();
  const [model, setModel] = useState<FragmentsModel | null>(null);
  const fragmentsRef = useRef<FragmentsModels | null>(null);
  const lastUpdateRef = useRef<number>(0);
  const highlightedRef = useRef<number | null>(null);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      const workerURL = await FragmentsModels.getWorker();
      if (cancelled) return;

      const fragments = new FragmentsModels(workerURL);
      fragmentsRef.current = fragments;

      const loaded = await fragments.load(bytes, {
        modelId: 'village-os-model',
        camera: camera as THREE.PerspectiveCamera,
      });
      if (cancelled) {
        await fragments.dispose();
        return;
      }

      await fitCameraToModel(loaded, camera as THREE.PerspectiveCamera, orbitRef.current);
      // Stream in tiles for the current camera view. Without this, model.object
      // stays empty until an interaction triggers an internal view update.
      await fragments.update(true);
      invalidate();
      setModel(loaded);
    })().catch((err) => {
      if (!cancelled) console.error('Failed to load Fragments model', err);
    });

    return () => {
      cancelled = true;
      const fragments = fragmentsRef.current;
      fragmentsRef.current = null;
      if (fragments) void fragments.dispose();
    };
  }, [bytes, camera, orbitRef, invalidate]);

  // Keep tiles fresh as the camera moves. Throttle to ~6Hz — the internal
  // worker coalesces requests, and every-frame updates saturate it.
  useFrame(({ clock }) => {
    const fragments = fragmentsRef.current;
    if (!fragments || !model) return;
    const now = clock.elapsedTime;
    if (now - lastUpdateRef.current < 1 / 6) return;
    lastUpdateRef.current = now;
    void fragments.update();
  });

  // Click-to-pick: raycast the Fragments model on pointer click, look up the
  // hit element's IFC GlobalId, and translate it to a VosThing via the
  // mapping sidecar. Distinguish clicks from orbit drags by measuring
  // pointer travel between down and up.
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
      if (dx > CLICK_MAX_DRAG_PX || dy > CLICK_MAX_DRAG_PX) return; // was a drag
      if (e.button !== 0) return; // primary only

      const rect = canvas.getBoundingClientRect();
      const mouse = new THREE.Vector2(
        ((e.clientX - rect.left) / rect.width) * 2 - 1,
        -((e.clientY - rect.top) / rect.height) * 2 + 1,
      );

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
      if (highlightedRef.current != null) {
        await model.highlight(undefined, HIGHLIGHT_MATERIAL);
      }
      await model.highlight([localId], HIGHLIGHT_MATERIAL);
      highlightedRef.current = localId;
    };

    const clearHighlight = async () => {
      if (highlightedRef.current == null) return;
      await model.highlight(undefined, HIGHLIGHT_MATERIAL);
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

/**
 * Frame the loaded model: position the camera so the bounding sphere fits
 * the view, and point OrbitControls at the model center so pan/rotate feel
 * natural.
 */
async function fitCameraToModel(
  model: FragmentsModel,
  camera: THREE.PerspectiveCamera,
  controls: OrbitControlsImpl | null,
): Promise<void> {
  const boxes = await model.getBoxes();
  if (!boxes.length) return;

  const total = boxes.reduce(
    (acc, b) => acc.union(b),
    new THREE.Box3().makeEmpty(),
  );
  const center = new THREE.Vector3();
  const size = new THREE.Vector3();
  total.getCenter(center);
  total.getSize(size);

  // Fit using the largest horizontal dimension — buildings are usually wide
  // and flat, so the vertical axis rarely dominates the view.
  const horizontal = Math.max(size.x, size.z, size.y) || 1;
  const distance = horizontal / (2 * Math.tan((camera.fov * Math.PI) / 360)) * 1.2;

  // Look from a 3/4 overhead angle so vertical structure is legible.
  const offset = new THREE.Vector3(distance * 0.8, distance * 0.6, distance * 0.8);
  camera.position.copy(center).add(offset);
  camera.near = Math.max(distance / 1000, 0.01);
  camera.far = distance * 1000;
  camera.updateProjectionMatrix();
  camera.lookAt(center);

  if (controls) {
    controls.target.copy(center);
    controls.update();
  }
}
