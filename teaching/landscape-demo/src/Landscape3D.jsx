import React, { useMemo } from 'react';
import { Canvas } from '@react-three/fiber';
import { OrbitControls } from '@react-three/drei';

function Cube({ x, y, h }) {
  const color = useMemo(() => {
    const t = h / 9;
    const r = Math.round(100 + 100 * (1 - t));
    const g = Math.round(150 + 80 * t);
    const b = Math.round(200 - 100 * t);
    return `rgb(${r},${g},${b})`;
  }, [h]);

  return (
    <mesh position={[x, h / 2, y]}>
      <boxGeometry args={[1, Math.max(0.1, h), 1]} />
      <meshStandardMaterial color={color} />
    </mesh>
  );
}

export default function Landscape3D({ grid }) {
  const width = grid.length;
  const depth = grid[0].length;
  return (
    <div style={{ width: '600px', height: '500px', background: '#eef', borderRadius: 8 }}>
      <Canvas camera={{ position: [width / 2, 20, depth * 1.2], fov: 50 }}>
        <ambientLight intensity={0.6} />
        <directionalLight position={[10, 20, 10]} intensity={0.8} />
        <group position={[-width / 2, 0, -depth / 2]}>
          {grid.map((row, i) =>
            row.map((h, j) => (
              <Cube key={`${i}-${j}`} x={i} y={j} h={h} />
            ))
          )}
          <mesh rotation={[-Math.PI / 2, 0, 0]} position={[width / 2, -0.05, depth / 2]}>
            <planeGeometry args={[width + 2, depth + 2]} />
            <meshStandardMaterial color="#dde" />
          </mesh>
        </group>
        <OrbitControls enablePan={true} enableZoom={true} />
      </Canvas>
    </div>
  );
}


