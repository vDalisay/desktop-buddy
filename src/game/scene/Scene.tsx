import { OrbitControls, PerspectiveCamera } from "@react-three/drei";
import { Physics, CuboidCollider, RigidBody } from "@react-three/rapier";
import { DragController, MannequinBuddy } from "../buddy/MannequinBuddy";
import { ToySpawner } from "../toys/ToySpawner";

export function Scene() {
  return (
    <>
      <PerspectiveCamera makeDefault position={[0, 1.25, 5.2]} fov={42} />
      <ambientLight intensity={0.9} />
      <directionalLight position={[2.5, 4, 3]} intensity={1.8} castShadow />
      <Physics gravity={[0, -9.81, 0]} colliders={false}>
        <RigidBody type="fixed" position={[0, -0.88, 0]} colliders={false}>
          <CuboidCollider args={[3.0, 0.12, 1.4]} />
          <mesh receiveShadow>
            <boxGeometry args={[6, 0.12, 2.8]} />
            <meshStandardMaterial color="#1e232b" transparent opacity={0.42} roughness={0.7} />
          </mesh>
        </RigidBody>
        <RigidBody type="fixed" position={[-3.1, 0.7, 0]} colliders={false}>
          <CuboidCollider args={[0.08, 1.7, 1.4]} />
        </RigidBody>
        <RigidBody type="fixed" position={[3.1, 0.7, 0]} colliders={false}>
          <CuboidCollider args={[0.08, 1.7, 1.4]} />
        </RigidBody>
        <RigidBody type="fixed" position={[0, 0.7, -1.45]} colliders={false}>
          <CuboidCollider args={[3.0, 1.7, 0.08]} />
        </RigidBody>
        <MannequinBuddy />
        <ToySpawner />
        <DragController />
      </Physics>
      <OrbitControls enablePan={false} enableZoom={false} minPolarAngle={0.9} maxPolarAngle={1.55} />
    </>
  );
}
