import { contextBridge, ipcRenderer } from "electron";
import type { CornerPin, SaveData, UserSettings } from "./types";

const api = {
  save: {
    read: () => ipcRenderer.invoke("save:read") as Promise<SaveData>,
    write: (data: SaveData) => ipcRenderer.invoke("save:write", data) as Promise<void>
  },
  settings: {
    read: () => ipcRenderer.invoke("settings:read") as Promise<UserSettings>,
    write: (settings: UserSettings) => ipcRenderer.invoke("settings:write", settings) as Promise<void>,
    onChanged: (callback: (settings: UserSettings) => void) => {
      const listener = (_event: Electron.IpcRendererEvent, settings: UserSettings): void => callback(settings);
      ipcRenderer.on("settings:changed", listener);
      return () => ipcRenderer.removeListener("settings:changed", listener);
    }
  },
  window: {
    setAlwaysOnTop: (enabled: boolean) =>
      ipcRenderer.invoke("window:set-always-on-top", enabled) as Promise<void>,
    setClickThrough: (enabled: boolean) =>
      ipcRenderer.invoke("window:set-click-through", enabled) as Promise<void>,
    pinCorner: (corner: CornerPin | null) => ipcRenderer.invoke("window:pin-corner", corner) as Promise<void>,
    hide: () => ipcRenderer.invoke("window:hide") as Promise<void>
  },
  steam: {
    status: () => ipcRenderer.invoke("steam:status") as Promise<{ available: boolean; reason: string }>,
    unlockAchievement: (id: string) => ipcRenderer.invoke("steam:unlock-achievement", id) as Promise<void>,
    setStat: (id: string, value: number) => ipcRenderer.invoke("steam:set-stat", id, value) as Promise<void>
  }
};

contextBridge.exposeInMainWorld("desktopBuddy", api);
