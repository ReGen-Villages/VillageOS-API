import { useEffect, useMemo } from 'react';
import { Canvas } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';
import * as THREE from 'three';
import type { RaisedSurface } from './surfaceGeometry';

/** The surface in one mesh, lit from above, turned with the pointer. Loaded only when a raised surface is shown. */
export default function RaisedSurfaceScene({ surface, colours }: { surface: RaisedSurface; colours: string[] }) {
  const geometry = useMemo(() => {
    const built = new THREE.BufferGeometry();
    built.setAttribute('position', new THREE.BufferAttribute(surface.positions, 3));
    const rgb = new Float32Array(colours.length * 3);
    colours.forEach((colour, at) => new THREE.Color(colour).toArray(rgb, at * 3));
    built.setAttribute('color', new THREE.BufferAttribute(rgb, 3));
    built.setIndex(new THREE.BufferAttribute(surface.indices, 1));
    built.computeVertexNormals();
    return built;
  }, [surface, colours]);
  useEffect(() => () => geometry.dispose(), [geometry]);

  const across = Math.max(surface.width, surface.depth);
  return (
    <Canvas camera={{ position: [across * 0.7, across * 0.8, across * 0.9], fov: 40, near: across / 1000, far: across * 10 }}>
      <ambientLight intensity={0.6} />
      <directionalLight position={[across, across * 2, across]} intensity={0.8} />
      <mesh geometry={geometry}>
        <meshStandardMaterial vertexColors side={THREE.DoubleSide} />
      </mesh>
      <OrbitControls target={[0, surface.boxHeight / 2, 0]} />
    </Canvas>
  );
}
