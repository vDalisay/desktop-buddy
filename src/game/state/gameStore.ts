import { create } from "zustand";
import type { RapierRigidBody } from "@react-three/rapier";
import type { CornerPin, SaveData, UserSettings } from "../../../electron/types";
import { defaultSaveData, defaultSettings } from "../../../electron/types";

export type ToolId = "grab" | "poke" | "paint" | "spawn";
export type ToyKind = "rubber-ball" | "heavy-cube" | "spring-pad";

export type SpawnedToy = {
  id: string;
  kind: ToyKind;
  position: [number, number, number];
};

export type GrabState = {
  body: RapierRigidBody;
  depth: number;
};

type GameState = {
  activeTool: ToolId;
  selectedToy: ToyKind;
  grabbed: GrabState | null;
  toys: SpawnedToy[];
  currency: number;
  settings: UserSettings;
  steamAvailable: boolean;
  setActiveTool: (tool: ToolId) => void;
  setSelectedToy: (toy: ToyKind) => void;
  setGrabbed: (grabbed: GrabState | null) => void;
  spawnToy: () => void;
  clearToys: () => void;
  addCurrency: (amount: number) => void;
  setSettings: (settings: UserSettings) => void;
  setSteamAvailable: (available: boolean) => void;
  setPinnedCorner: (corner: CornerPin | null) => void;
  toSaveData: () => SaveData;
};

export const useGameStore = create<GameState>((set, get) => ({
  activeTool: "grab",
  selectedToy: "rubber-ball",
  grabbed: null,
  toys: [],
  currency: 0,
  settings: defaultSettings,
  steamAvailable: false,
  setActiveTool: (tool) => set({ activeTool: tool }),
  setSelectedToy: (toy) => set({ selectedToy: toy }),
  setGrabbed: (grabbed) => set({ grabbed }),
  spawnToy: () => {
    const state = get();
    set({
      toys: [
        ...state.toys,
        {
          id: crypto.randomUUID(),
          kind: state.selectedToy,
          position: [(Math.random() - 0.5) * 2.4, 2.2, (Math.random() - 0.5) * 0.4]
        }
      ]
    });
  },
  clearToys: () => set({ toys: [] }),
  addCurrency: (amount) => set((state) => ({ currency: Math.max(0, state.currency + amount) })),
  setSettings: (settings) => set({ settings }),
  setSteamAvailable: (available) => set({ steamAvailable: available }),
  setPinnedCorner: (corner) =>
    set((state) => ({
      settings: {
        ...state.settings,
        window: {
          ...state.settings.window,
          pinnedCorner: corner
        }
      }
    })),
  toSaveData: () => {
    const state = get();
    return {
      ...defaultSaveData,
      currency: state.currency,
      settings: state.settings,
      lastSeenAt: new Date().toISOString()
    };
  }
}));

