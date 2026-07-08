export type BuddyDefinition = {
  id: string;
  displayName: string;
  rigPreset: "humanoid-mannequin";
  modelPath: string | null;
  materialSlots: string[];
  reactionSet: "prototype";
};

export const prototypeBuddy: BuddyDefinition = {
  id: "prototype-mannequin",
  displayName: "Prototype Buddy",
  rigPreset: "humanoid-mannequin",
  modelPath: null,
  materialSlots: ["body", "paint"],
  reactionSet: "prototype"
};

