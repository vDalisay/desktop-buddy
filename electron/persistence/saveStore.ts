import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { app } from "electron";
import { defaultSaveData, defaultSettings, type SaveData, type UserSettings } from "../types";

const savePath = (): string => join(app.getPath("userData"), "save-data.json");
const settingsPath = (): string => join(app.getPath("userData"), "settings.json");

async function readJson<T>(path: string, fallback: T): Promise<T> {
  try {
    const raw = await readFile(path, "utf8");
    return JSON.parse(raw) as T;
  } catch {
    return fallback;
  }
}

async function writeJson(path: string, data: unknown): Promise<void> {
  await mkdir(dirname(path), { recursive: true });
  await writeFile(path, JSON.stringify(data, null, 2), "utf8");
}

export async function readSaveData(): Promise<SaveData> {
  const save = await readJson<SaveData>(savePath(), defaultSaveData);
  return {
    ...defaultSaveData,
    ...save,
    settings: {
      ...defaultSettings,
      ...save.settings,
      window: { ...defaultSettings.window, ...save.settings?.window },
      matureContent: { ...defaultSettings.matureContent, ...save.settings?.matureContent },
      accessibility: { ...defaultSettings.accessibility, ...save.settings?.accessibility }
    }
  };
}

export async function writeSaveData(data: SaveData): Promise<void> {
  await writeJson(savePath(), { ...data, lastSeenAt: new Date().toISOString() });
}

export async function readSettings(): Promise<UserSettings> {
  const settings = await readJson<UserSettings>(settingsPath(), defaultSettings);
  return {
    ...defaultSettings,
    ...settings,
    window: { ...defaultSettings.window, ...settings.window },
    matureContent: { ...defaultSettings.matureContent, ...settings.matureContent },
    accessibility: { ...defaultSettings.accessibility, ...settings.accessibility }
  };
}

export async function writeSettings(settings: UserSettings): Promise<void> {
  await writeJson(settingsPath(), settings);
}

