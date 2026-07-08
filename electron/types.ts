export type CornerPin = "top-left" | "top-right" | "bottom-left" | "bottom-right";

export type WindowSettings = {
  alwaysOnTop: boolean;
  clickThrough: boolean;
  pinnedCorner: CornerPin | null;
};

export type UserSettings = {
  window: WindowSettings;
  matureContent: {
    bloodEnabled: boolean;
  };
  accessibility: {
    reducedMotion: boolean;
    uiScale: number;
  };
};

export type SaveData = {
  schemaVersion: 1;
  currency: number;
  unlockedToyIds: string[];
  selectedBuddyId: string;
  settings: UserSettings;
  lastSeenAt: string;
};

export const defaultSettings: UserSettings = {
  window: {
    alwaysOnTop: true,
    clickThrough: false,
    pinnedCorner: null
  },
  matureContent: {
    bloodEnabled: false
  },
  accessibility: {
    reducedMotion: false,
    uiScale: 1
  }
};

export const defaultSaveData: SaveData = {
  schemaVersion: 1,
  currency: 0,
  unlockedToyIds: ["rubber-ball", "heavy-cube", "spring-pad"],
  selectedBuddyId: "prototype-mannequin",
  settings: defaultSettings,
  lastSeenAt: new Date(0).toISOString()
};

