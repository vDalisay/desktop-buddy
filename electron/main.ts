import { app, BrowserWindow, globalShortcut, ipcMain } from "electron";
import { createGameWindow } from "./window/createGameWindow";
import { pinWindowToCorner } from "./window/windowState";
import { readSaveData, readSettings, writeSaveData, writeSettings } from "./persistence/saveStore";
import { getSteamStatus, setStat, unlockAchievement } from "./steam/steamClient";
import type { CornerPin, SaveData, UserSettings } from "./types";

let mainWindow: BrowserWindow | null = null;
let activeSettings: UserSettings | null = null;

function getMainWindow(): BrowserWindow {
  if (!mainWindow) {
    throw new Error("Main window is not available.");
  }
  return mainWindow;
}

async function createApp(): Promise<void> {
  mainWindow = createGameWindow();
  const settings = await readSettings();
  activeSettings = settings;

  mainWindow.webContents.on("console-message", (_event, level, message, line, sourceId) => {
    console.info(`[renderer:${level}] ${message} (${sourceId}:${line})`);
  });

  mainWindow.webContents.on("render-process-gone", (_event, details) => {
    console.error("[renderer] process gone", details);
  });

  mainWindow.webContents.on("did-fail-load", (_event, code, description) => {
    console.error(`[renderer] load failed ${code}: ${description}`);
  });

  mainWindow.setAlwaysOnTop(settings.window.alwaysOnTop, "floating");
  mainWindow.setIgnoreMouseEvents(settings.window.clickThrough, { forward: true });

  if (settings.window.pinnedCorner) {
    pinWindowToCorner(mainWindow, settings.window.pinnedCorner);
  }

  globalShortcut.register("CommandOrControl+Shift+B", () => {
    void toggleClickThroughFromShortcut();
  });
}

const gotLock = app.requestSingleInstanceLock();

if (!gotLock) {
  app.quit();
} else {
  app.on("second-instance", () => {
    if (mainWindow) {
      if (mainWindow.isMinimized()) mainWindow.restore();
      mainWindow.focus();
    }
  });

  app.whenReady().then(createApp).catch((error) => {
    console.error(error);
    app.quit();
  });
}

app.on("window-all-closed", () => {
  app.quit();
});

app.on("will-quit", () => {
  globalShortcut.unregisterAll();
});

async function toggleClickThroughFromShortcut(): Promise<void> {
  const win = getMainWindow();
  const settings = activeSettings ?? (await readSettings());
  const next: UserSettings = {
    ...settings,
    window: {
      ...settings.window,
      clickThrough: !settings.window.clickThrough
    }
  };

  activeSettings = next;
  win.setIgnoreMouseEvents(next.window.clickThrough, { forward: true });
  win.webContents.send("settings:changed", next);
  await writeSettings(next);
}

ipcMain.handle("save:read", () => readSaveData());
ipcMain.handle("save:write", (_event, data: SaveData) => writeSaveData(data));
ipcMain.handle("settings:read", () => readSettings());
ipcMain.handle("settings:write", (_event, settings: UserSettings) => {
  activeSettings = settings;
  return writeSettings(settings);
});

ipcMain.handle("window:set-always-on-top", (_event, enabled: boolean) => {
  getMainWindow().setAlwaysOnTop(enabled, "floating");
});

ipcMain.handle("window:set-click-through", (_event, enabled: boolean) => {
  if (activeSettings) {
    activeSettings = {
      ...activeSettings,
      window: {
        ...activeSettings.window,
        clickThrough: enabled
      }
    };
  }
  getMainWindow().setIgnoreMouseEvents(enabled, { forward: true });
});

ipcMain.handle("window:pin-corner", (_event, corner: CornerPin | null) => {
  if (corner) {
    pinWindowToCorner(getMainWindow(), corner);
  }
});

ipcMain.handle("window:hide", () => {
  getMainWindow().hide();
});

ipcMain.handle("steam:status", () => getSteamStatus());
ipcMain.handle("steam:unlock-achievement", (_event, id: string) => unlockAchievement(id));
ipcMain.handle("steam:set-stat", (_event, id: string, value: number) => setStat(id, value));
