/**
 * Single-building 3D viewer for the detail panel.
 *
 * Renders a centered, auto-rotating building model with orbit controls.
 * Lazy-loaded when the "3D" tab is activated in NodeDetailPanel.
 *
 * Things with child elements (via spatial-containment predicates) render
 * multiple meshes colored by `colorKey` hash. The caller decides what
 * string to put in `colorKey` — the viewer is domain-agnostic.
 */
import { useMemo, useRef, useEffect } from 'react';
import { Canvas, useFrame } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import * as THREE from 'three';
import { parseSolidMesh, type SolidMeshData } from '../../utils/geometryDispatcher';
import { hashStringToIndex, ELEMENT_COLORS } from '../../utils/colors';

export interface ChildElement {
  geometryValue: unknown;
  name: string;
  /**
   * Arbitrary string used to deterministically pick a mesh color via
   * hashing into ELEMENT_COLORS. Children sharing a colorKey share a
   * color; the meaning of the key is the caller's choice (e.g. an IFC
   * class for IFC seeds, a `kind` property for another domain). When
   * absent the parent's `color` is used.
   */
  colorKey?: string;
}

interface BuildingDetail3DProps {
  geometryValue: unknown;
  color?: string;
  childElements?: ChildElement[];
}

function colorForKey(key: string): string {
  return ELEMENT_COLORS[hashStringToIndex(key, ELEMENT_COLORS.length)];
}

/** Parse and center a single mesh, returning the Three.js geometry. */
function buildCenteredGeometry(
  mesh: SolidMeshData,
  cx: number,
  cz: number,
): THREE.BufferGeometry {
  const centered = new Float32Array(mesh.positions.length);
  for (let i = 0; i < mesh.positions.length; i += 3) {
    centered[i] = mesh.positions[i] - cx;
    centered[i + 1] = mesh.positions[i + 1];
    centered[i + 2] = mesh.positions[i + 2] - cz;
  }

  const geom = new THREE.BufferGeometry();
  geom.setAttribute('position', new THREE.BufferAttribute(centered, 3));
  geom.setAttribute('normal', new THREE.BufferAttribute(mesh.normals, 3));
  geom.setIndex(new THREE.BufferAttribute(mesh.indices, 1));
  return geom;
}

interface MeshEntry {
  geometry: THREE.BufferGeometry;
  color: string;
}

function RotatingBuilding({
  geometryValue,
  color,
  childElements,
}: {
  geometryValue: unknown;
  color: string;
  childElements?: ChildElement[];
}) {
  const groupRef = useRef<THREE.Group>(null);

  const meshEntries = useMemo(() => {
    const entries: MeshEntry[] = [];

    // Parse the primary mesh (may be null for container IFC things)
    const primaryMesh = parseSolidMesh(geometryValue);

    // Collect all meshes to compute a shared center
    const allMeshes: { mesh: SolidMeshData; color: string }[] = [];

    if (primaryMesh) {
      allMeshes.push({ mesh: primaryMesh, color });
    }

    if (childElements) {
      for (const child of childElements) {
        const childMesh = parseSolidMesh(child.geometryValue);
        if (!childMesh) continue;
        const childColor = child.colorKey
          ? colorForKey(child.colorKey)
          : color;
        allMeshes.push({ mesh: childMesh, color: childColor });
      }
    }

    if (allMeshes.length === 0) return entries;

    // Compute shared center across all meshes
    let sumCx = 0, sumCz = 0;
    for (const { mesh } of allMeshes) {
      sumCx += mesh.localCenter[0];
      sumCz += mesh.localCenter[1];
    }
    const cx = sumCx / allMeshes.length;
    const cz = sumCz / allMeshes.length;

    for (const { mesh, color: c } of allMeshes) {
      entries.push({
        geometry: buildCenteredGeometry(mesh, cx, cz),
        color: c,
      });
    }

    return entries;
  }, [geometryValue, color, childElements]);

  // Slow auto-rotation
  useFrame((_, delta) => {
    if (groupRef.current) {
      groupRef.current.rotation.y += delta * 0.3;
    }
  });

  // Cleanup geometries on unmount
  useEffect(() => {
    return () => {
      for (const entry of meshEntries) {
        entry.geometry.dispose();
      }
    };
  }, [meshEntries]);

  if (meshEntries.length === 0) return null;

  return (
    <group ref={groupRef}>
      {meshEntries.map((entry, i) => (
        <mesh key={i} geometry={entry.geometry}>
          <meshStandardMaterial
            color={entry.color}
            roughness={0.6}
            metalness={0.15}
            flatShading
          />
        </mesh>
      ))}
    </group>
  );
}

export default function BuildingDetail3D({
  geometryValue,
  color = '#6d8ea8',
  childElements,
}: BuildingDetail3DProps) {
  // Parse all meshes for camera computation
  const allMeshes = useMemo(() => {
    const meshes: SolidMeshData[] = [];
    const primary = parseSolidMesh(geometryValue);
    if (primary) meshes.push(primary);
    if (childElements) {
      for (const child of childElements) {
        const m = parseSolidMesh(child.geometryValue);
        if (m) meshes.push(m);
      }
    }
    return meshes;
  }, [geometryValue, childElements]);

  // Must precede the early return below: hooks have to run in the same order
  // every render, so both memos stay above any conditional exit.
  const canvasKey = useMemo(() => JSON.stringify(geometryValue), [geometryValue]);

  if (allMeshes.length === 0) {
    return (
      <div className="h-full flex items-center justify-center text-zinc-500 text-sm">
        Unable to parse 3D geometry.
      </div>
    );
  }

  // Compute camera distance from combined bounding box
  let sumCx = 0, sumCz = 0;
  for (const m of allMeshes) {
    sumCx += m.localCenter[0];
    sumCz += m.localCenter[1];
  }
  const cx = sumCx / allMeshes.length;
  const cz = sumCz / allMeshes.length;

  let minX = Infinity, maxX = -Infinity;
  let minZ = Infinity, maxZ = -Infinity;
  let maxHeight = 0;
  for (const m of allMeshes) {
    const pos = m.positions;
    for (let i = 0; i < pos.length; i += 3) {
      const x = pos[i] - cx;
      const z = pos[i + 2] - cz;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (z < minZ) minZ = z;
      if (z > maxZ) maxZ = z;
    }
    if (m.height > maxHeight) maxHeight = m.height;
  }
  const extentX = maxX - minX;
  const extentZ = maxZ - minZ;
  const maxDim = Math.max(maxHeight, extentX, extentZ, 2);
  const camDistance = maxDim * 2;

  return (
    <div className="h-64 w-full rounded-lg overflow-hidden bg-zinc-950">
      <Canvas
        key={canvasKey}
        camera={{
          fov: 45,
          near: 0.1,
          far: camDistance * 10,
          position: [camDistance * 0.7, camDistance * 0.5, camDistance * 0.7],
        }}
        gl={{ antialias: true, alpha: false, preserveDrawingBuffer: false }}
        style={{ background: '#09090b' }}
        dpr={[1, 2]}
        frameloop="always"
      >
        <ambientLight color="#1e1e2e" intensity={0.6} />
        <directionalLight position={[10, 15, 10]} color="#e8d5b7" intensity={0.9} />
        <hemisphereLight color="#334155" groundColor="#0f0f14" intensity={0.3} />

        <RotatingBuilding
          geometryValue={geometryValue}
          color={color}
          childElements={childElements}
        />

        <OrbitControls
          enablePan={false}
          maxPolarAngle={Math.PI / 2 - 0.05}
          minDistance={1}
          maxDistance={camDistance * 3}
          enableDamping
          dampingFactor={0.1}
        />
      </Canvas>
    </div>
  );
}
