import type { BrowserWindow, Rectangle } from "electron";
import { screen } from "electron";
import type { CornerPin } from "../types";

const margin = 24;

export function pinWindowToCorner(win: BrowserWindow, corner: CornerPin): void {
  const display = screen.getDisplayNearestPoint(screen.getCursorScreenPoint());
  const area = display.workArea;
  const bounds = win.getBounds();
  const next: Pick<Rectangle, "x" | "y"> = {
    x: area.x + margin,
    y: area.y + margin
  };

  if (corner.includes("right")) {
    next.x = area.x + area.width - bounds.width - margin;
  }

  if (corner.includes("bottom")) {
    next.y = area.y + area.height - bounds.height - margin;
  }

  win.setPosition(Math.round(next.x), Math.round(next.y), true);
}

