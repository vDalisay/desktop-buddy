import type { CornerPin, SaveData, UserSettings } from "../../electron/types";

declare global {
  interface Window {
    desktopBuddy: {
      save: {
        read(): Promise<SaveData>;
        write(data: SaveData): Promise<void>;
      };
      settings: {
        read(): Promise<UserSettings>;
        write(settings: UserSettings): Promise<void>;
        onChanged(callback: (settings: UserSettings) => void): () => void;
      };
      window: {
        setAlwaysOnTop(enabled: boolean): Promise<void>;
        setClickThrough(enabled: boolean): Promise<void>;
        pinCorner(corner: CornerPin | null): Promise<void>;
        hide(): Promise<void>;
      };
      steam: {
        status(): Promise<{ available: boolean; reason: string }>;
        unlockAchievement(id: string): Promise<void>;
        setStat(id: string, value: number): Promise<void>;
      };
    };
  }
}

export {};
