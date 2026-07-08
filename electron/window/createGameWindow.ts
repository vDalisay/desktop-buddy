import { join } from "node:path";
import { BrowserWindow } from "electron";

export function createGameWindow(): BrowserWindow {
  const win = new BrowserWindow({
    width: 620,
    height: 520,
    minWidth: 420,
    minHeight: 360,
    transparent: true,
    backgroundColor: "#00000000",
    frame: false,
    resizable: true,
    alwaysOnTop: true,
    hasShadow: false,
    title: "Desktop Buddy",
    webPreferences: {
      preload: join(__dirname, "../preload/index.mjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false
    }
  });

  win.setAlwaysOnTop(true, "floating");

  if (process.env.ELECTRON_RENDERER_URL) {
    void win.loadURL(process.env.ELECTRON_RENDERER_URL);
  } else {
    void win.loadFile(join(__dirname, "../renderer/index.html"));
  }

  return win;
}
