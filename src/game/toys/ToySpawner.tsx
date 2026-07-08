import { BallCollider, CuboidCollider, RigidBody } from "@react-three/rapier";
import { useGameStore, type SpawnedToy } from "../state/gameStore";

function Toy({ toy }: { toy: SpawnedToy }) {
  if (toy.kind === "rubber-ball") {
    return (
      <RigidBody position={toy.position} colliders={false} restitution={0.9} friction={0.45} linearDamping={0.25}>
        <BallCollider args={[0.18]} />
        <mesh castShadow receiveShadow>
          <sphereGeometry args={[0.18, 32, 20]} />
          <meshStandardMaterial color="#ff4f7b" roughness={0.5} />
        </mesh>
      </RigidBody>
    );
  }

  if (toy.kind === "spring-pad") {
    return (
      <RigidBody position={toy.position} colliders={false} restitution={1.4} friction={0.35}>
        <CuboidCollider args={[0.35, 0.08, 0.35]} />
        <mesh castShadow receiveShadow>
          <boxGeometry args={[0.7, 0.16, 0.7]} />
          <meshStandardMaterial color="#46e0b4" emissive="#11382f" roughness={0.45} />
        </mesh>
      </RigidBody>
    );
  }

  return (
    <RigidBody position={toy.position} colliders={false} restitution={0.15} friction={0.85} linearDamping={0.4}>
      <CuboidCollider args={[0.22, 0.22, 0.22]} />
      <mesh castShadow receiveShadow>
        <boxGeometry args={[0.44, 0.44, 0.44]} />
        <meshStandardMaterial color="#d6dde5" metalness={0.25} roughness={0.35} />
      </mesh>
    </RigidBody>
  );
}

export function ToySpawner() {
  const toys = useGameStore((state) => state.toys);
  return (
    <>
      {toys.map((toy) => (
        <Toy key={toy.id} toy={toy} />
      ))}
    </>
  );
}
