import { resolve } from "node:path";
import { defineConfig, externalizeDepsPlugin } from "electron-vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  main: {
    plugins: [externalizeDepsPlugin()],
    build: {
      rollupOptions: {
        input: {
          index: resolve("electron/main.ts")
        }
      }
    }
  },
  preload: {
    plugins: [externalizeDepsPlugin()],
    build: {
      rollupOptions: {
        input: {
          index: resolve("electron/preload.ts")
        }
      }
    }
  },
  renderer: {
    root: ".",
    resolve: {
      alias: {
        "@renderer": resolve("src")
      }
    },
    plugins: [react()],
    build: {
      rollupOptions: {
        input: {
          index: resolve("index.html")
        }
      }
    }
  }
});
