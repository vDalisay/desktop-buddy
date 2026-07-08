import { ThreeEvent, useFrame, useThree } from "@react-three/fiber";
import {
  BallCollider,
  CapsuleCollider,
  CuboidCollider,
  RapierRigidBody,
  RigidBody,
  useSphericalJoint
} from "@react-three/rapier";
import { RefObject, createRef, useEffect, useMemo, useState } from "react";
import * as THREE from "three";
import { useGameStore } from "../state/gameStore";

type PartName =
  | "head"
  | "torso"
  | "pelvis"
  | "leftUpperArm"
  | "leftLowerArm"
  | "rightUpperArm"
  | "rightLowerArm"
  | "leftUpperLeg"
  | "leftLowerLeg"
  | "rightUpperLeg"
  | "rightLowerLeg";

type PartConfig = {
  name: PartName;
  position: [number, number, number];
  color: string;
  shape: "sphere" | "capsule" | "box";
  scale: [number, number, number];
  collider: "ball" | "capsule" | "cuboid";
};

const parts: PartConfig[] = [
  { name: "head", position: [0, 2.35, 0], color: "#f7d7b5", shape: "sphere", scale: [0.32, 0.32, 0.32], collider: "ball" },
  { name: "torso", position: [0, 1.58, 0], color: "#77b7ff", shape: "capsule", scale: [0.42, 0.58, 0.42], collider: "capsule" },
  { name: "pelvis", position: [0, 0.92, 0], color: "#6695c8", shape: "box", scale: [0.5, 0.3, 0.32], collider: "cuboid" },
  { name: "leftUpperArm", position: [-0.58, 1.72, 0], color: "#ffd25f", shape: "capsule", scale: [0.15, 0.38, 0.15], collider: "capsule" },
  { name: "leftLowerArm", position: [-0.92, 1.22, 0], color: "#f5b942", shape: "capsule", scale: [0.14, 0.34, 0.14], collider: "capsule" },
  { name: "rightUpperArm", position: [0.58, 1.72, 0], color: "#ffd25f", shape: "capsule", scale: [0.15, 0.38, 0.15], collider: "capsule" },
  { name: "rightLowerArm", position: [0.92, 1.22, 0], color: "#f5b942", shape: "capsule", scale: [0.14, 0.34, 0.14], collider: "capsule" },
  { name: "leftUpperLeg", position: [-0.24, 0.42, 0], color: "#8f73ff", shape: "capsule", scale: [0.17, 0.42, 0.17], collider: "capsule" },
  { name: "leftLowerLeg", position: [-0.3, -0.28, 0], color: "#7259d6", shape: "capsule", scale: [0.16, 0.42, 0.16], collider: "capsule" },
  { name: "rightUpperLeg", position: [0.24, 0.42, 0], color: "#8f73ff", shape: "capsule", scale: [0.17, 0.42, 0.17], collider: "capsule" },
  { name: "rightLowerLeg", position: [0.3, -0.28, 0], color: "#7259d6", shape: "capsule", scale: [0.16, 0.42, 0.16], collider: "capsule" }
];

function makeRefs(): Record<PartName, RefObject<RapierRigidBody>> {
  return Object.fromEntries(parts.map((part) => [part.name, createRef<RapierRigidBody>()])) as Record<
    PartName,
    RefObject<RapierRigidBody>
  >;
}

function BuddyJoints({ refs }: { refs: Record<PartName, RefObject<RapierRigidBody>> }): null {
  useSphericalJoint(refs.head, refs.torso, [
    [0, -0.26, 0],
    [0, 0.45, 0]
  ]);
  useSphericalJoint(refs.torso, refs.pelvis, [
    [0, -0.45, 0],
    [0, 0.2, 0]
  ]);
  useSphericalJoint(refs.torso, refs.leftUpperArm, [
    [-0.34, 0.22, 0],
    [0, 0.28, 0]
  ]);
  useSphericalJoint(refs.leftUpperArm, refs.leftLowerArm, [
    [0, -0.26, 0],
    [0, 0.24, 0]
  ]);
  useSphericalJoint(refs.torso, refs.rightUpperArm, [
    [0.34, 0.22, 0],
    [0, 0.28, 0]
  ]);
  useSphericalJoint(refs.rightUpperArm, refs.rightLowerArm, [
    [0, -0.26, 0],
    [0, 0.24, 0]
  ]);
  useSphericalJoint(refs.pelvis, refs.leftUpperLeg, [
    [-0.18, -0.2, 0],
    [0, 0.3, 0]
  ]);
  useSphericalJoint(refs.leftUpperLeg, refs.leftLowerLeg, [
    [0, -0.3, 0],
    [0, 0.3, 0]
  ]);
  useSphericalJoint(refs.pelvis, refs.rightUpperLeg, [
    [0.18, -0.2, 0],
    [0, 0.3, 0]
  ]);
  useSphericalJoint(refs.rightUpperLeg, refs.rightLowerLeg, [
    [0, -0.3, 0],
    [0, 0.3, 0]
  ]);

  return null;
}

function PartCollider({ part }: { part: PartConfig }) {
  if (part.collider === "ball") {
    return <BallCollider args={[part.scale[0]]} />;
  }

  if (part.collider === "cuboid") {
    return <CuboidCollider args={[part.scale[0], part.scale[1], part.scale[2]]} />;
  }

  return <CapsuleCollider args={[part.scale[1], part.scale[0]]} />;
}

function PartMesh({ part, color }: { part: PartConfig; color: string }) {
  if (part.shape === "sphere") {
    return (
      <mesh castShadow receiveShadow>
        <sphereGeometry args={[part.scale[0], 32, 20]} />
        <meshStandardMaterial color={color} roughness={0.55} />
      </mesh>
    );
  }

  if (part.shape === "box") {
    return (
      <mesh castShadow receiveShadow>
        <boxGeometry args={[part.scale[0] * 2, part.scale[1] * 2, part.scale[2] * 2]} />
        <meshStandardMaterial color={color} roughness={0.6} />
      </mesh>
    );
  }

  return (
    <mesh castShadow receiveShadow>
      <capsuleGeometry args={[part.scale[0], part.scale[1] * 2, 8, 20]} />
      <meshStandardMaterial color={color} roughness={0.62} />
    </mesh>
  );
}

export function DragController(): null {
  const grabbed = useGameStore((state) => state.grabbed);
  const setGrabbed = useGameStore((state) => state.setGrabbed);
  const { camera, pointer } = useThree();
  const raycaster = useMemo(() => new THREE.Raycaster(), []);
  const plane = useMemo(() => new THREE.Plane(new THREE.Vector3(0, 0, 1), 0), []);
  const target = useMemo(() => new THREE.Vector3(), []);

  useFrame(() => {
    if (!grabbed) return;

    raycaster.setFromCamera(pointer, camera);
    raycaster.ray.intersectPlane(plane, target);
    const current = grabbed.body.translation();
    const impulse = {
      x: (target.x - current.x) * 0.85,
      y: (target.y - current.y) * 0.85,
      z: (target.z - current.z) * 0.85
    };
    grabbed.body.applyImpulse(impulse, true);
    grabbed.body.setLinvel(
      {
        x: impulse.x * 7,
        y: impulse.y * 7,
        z: impulse.z * 7
      },
      true
    );
  });

  useEffect(() => {
    const clear = () => setGrabbed(null);
    window.addEventListener("pointerup", clear);
    return () => window.removeEventListener("pointerup", clear);
  }, [setGrabbed]);

  return null;
}

export function MannequinBuddy() {
  const refs = useMemo(makeRefs, []);
  const activeTool = useGameStore((state) => state.activeTool);
  const setGrabbed = useGameStore((state) => state.setGrabbed);
  const addCurrency = useGameStore((state) => state.addCurrency);
  const [paintedParts, setPaintedParts] = useState<Partial<Record<PartName, string>>>({});
  const { camera } = useThree();

  const onPartPointerDown = (event: ThreeEvent<PointerEvent>, part: PartConfig): void => {
    event.stopPropagation();
    const body = refs[part.name].current;
    if (!body) return;

    if (activeTool === "grab") {
      setGrabbed({ body, depth: event.distance });
      addCurrency(1);
      return;
    }

    if (activeTool === "poke") {
      const direction = new THREE.Vector3();
      camera.getWorldDirection(direction);
      body.applyImpulse({ x: direction.x * 0.65, y: direction.y * 0.65 + 0.15, z: direction.z * 0.65 }, true);
      addCurrency(2);
      return;
    }

    if (activeTool === "paint") {
      setPaintedParts((current) => ({
        ...current,
        [part.name]: current[part.name] === "#f05f7f" ? part.color : "#f05f7f"
      }));
      addCurrency(1);
    }
  };

  return (
    <group>
      <BuddyJoints refs={refs} />
      {parts.map((part) => (
        <RigidBody
          key={part.name}
          ref={refs[part.name]}
          colliders={false}
          position={part.position}
          linearDamping={1.6}
          angularDamping={2.1}
          canSleep={false}
        >
          <PartCollider part={part} />
          <group onPointerDown={(event) => onPartPointerDown(event, part)}>
            <PartMesh part={part} color={paintedParts[part.name] ?? part.color} />
          </group>
        </RigidBody>
      ))}
    </group>
  );
}
