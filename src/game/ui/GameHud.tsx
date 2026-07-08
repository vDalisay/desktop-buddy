import {
  ArrowDownLeft,
  ArrowDownRight,
  ArrowUpLeft,
  ArrowUpRight,
  Box,
  Brush,
  Crosshair,
  Hand,
  MousePointer2,
  Pin,
  RotateCcw,
  Shield,
  Sparkles,
  X
} from "lucide-react";
import type { CornerPin } from "../../../electron/types";
import { useGameStore, type ToolId, type ToyKind } from "../state/gameStore";

const tools: Array<{ id: ToolId; label: string; Icon: typeof Hand }> = [
  { id: "grab", label: "Grab", Icon: Hand },
  { id: "poke", label: "Poke", Icon: Crosshair },
  { id: "paint", label: "Paint", Icon: Brush },
  { id: "spawn", label: "Spawn", Icon: Box }
];

const toys: Array<{ id: ToyKind; label: string }> = [
  { id: "rubber-ball", label: "Ball" },
  { id: "heavy-cube", label: "Cube" },
  { id: "spring-pad", label: "Spring" }
];

const corners: Array<{ id: CornerPin; label: string; Icon: typeof ArrowUpLeft }> = [
  { id: "top-left", label: "Top left", Icon: ArrowUpLeft },
  { id: "top-right", label: "Top right", Icon: ArrowUpRight },
  { id: "bottom-left", label: "Bottom left", Icon: ArrowDownLeft },
  { id: "bottom-right", label: "Bottom right", Icon: ArrowDownRight }
];

export function GameHud() {
  const activeTool = useGameStore((state) => state.activeTool);
  const selectedToy = useGameStore((state) => state.selectedToy);
  const currency = useGameStore((state) => state.currency);
  const steamAvailable = useGameStore((state) => state.steamAvailable);
  const settings = useGameStore((state) => state.settings);
  const setActiveTool = useGameStore((state) => state.setActiveTool);
  const setSelectedToy = useGameStore((state) => state.setSelectedToy);
  const spawnToy = useGameStore((state) => state.spawnToy);
  const clearToys = useGameStore((state) => state.clearToys);
  const setSettings = useGameStore((state) => state.setSettings);
  const setPinnedCorner = useGameStore((state) => state.setPinnedCorner);

  const updateSettings = async (next: typeof settings): Promise<void> => {
    setSettings(next);
    await window.desktopBuddy.settings.write(next);
  };

  const toggleAlwaysOnTop = async (): Promise<void> => {
    const enabled = !settings.window.alwaysOnTop;
    const next = { ...settings, window: { ...settings.window, alwaysOnTop: enabled } };
    await window.desktopBuddy.window.setAlwaysOnTop(enabled);
    await updateSettings(next);
  };

  const toggleClickThrough = async (): Promise<void> => {
    const enabled = !settings.window.clickThrough;
    const next = { ...settings, window: { ...settings.window, clickThrough: enabled } };
    await updateSettings(next);
    await window.desktopBuddy.window.setClickThrough(enabled);
  };

  const toggleBlood = async (): Promise<void> => {
    await updateSettings({
      ...settings,
      matureContent: { bloodEnabled: !settings.matureContent.bloodEnabled }
    });
  };

  const pinCorner = async (corner: CornerPin | null): Promise<void> => {
    setPinnedCorner(corner);
    const next = { ...settings, window: { ...settings.window, pinnedCorner: corner } };
    await updateSettings(next);
    await window.desktopBuddy.window.pinCorner(corner);
  };

  return (
    <div className="hud-shell">
      <div className="drag-strip">
        <MousePointer2 aria-hidden="true" size={18} />
        <span>Desktop Buddy</span>
        <span className="currency">
          <Sparkles aria-hidden="true" size={18} />
          {currency.toFixed(0)}
        </span>
      </div>

      <div className="toolbar" aria-label="Tool selection">
        {tools.map(({ id, label, Icon }) => (
          <button
            className={activeTool === id ? "icon-button active" : "icon-button"}
            key={id}
            title={label}
            aria-label={label}
            onClick={() => setActiveTool(id)}
          >
            <Icon aria-hidden="true" size={22} />
          </button>
        ))}
      </div>

      <div className="panel lower-panel">
        <div className="segmented" aria-label="Toy selection">
          {toys.map((toy) => (
            <button
              key={toy.id}
              className={selectedToy === toy.id ? "segment active" : "segment"}
              onClick={() => setSelectedToy(toy.id)}
            >
              {toy.label}
            </button>
          ))}
        </div>
        <button className="command-button" onClick={spawnToy}>
          Spawn
        </button>
        <button className="icon-button" title="Clear toys" aria-label="Clear toys" onClick={clearToys}>
          <RotateCcw aria-hidden="true" size={22} />
        </button>
      </div>

      <details className="panel settings-panel">
        <summary>
          <Pin aria-hidden="true" size={18} />
          Window
        </summary>
        <div className="settings-grid">
          <button className={settings.window.alwaysOnTop ? "toggle active" : "toggle"} onClick={toggleAlwaysOnTop}>
            Always on top
          </button>
          <button className={settings.window.clickThrough ? "toggle active" : "toggle"} onClick={toggleClickThrough}>
            Work mode
          </button>
          <button className={settings.matureContent.bloodEnabled ? "toggle active" : "toggle"} onClick={toggleBlood}>
            Blood effects
          </button>
          <div className="corner-grid" aria-label="Corner pinning">
            {corners.map(({ id, label, Icon }) => (
              <button
                key={id}
                className={settings.window.pinnedCorner === id ? "icon-button active" : "icon-button"}
                title={label}
                aria-label={label}
                onClick={() => pinCorner(id)}
              >
                <Icon aria-hidden="true" size={20} />
              </button>
            ))}
            <button className="icon-button" title="Unpin" aria-label="Unpin" onClick={() => pinCorner(null)}>
              <X aria-hidden="true" size={20} />
            </button>
          </div>
          <div className="steam-status">
            <Shield aria-hidden="true" size={18} />
            Steam {steamAvailable ? "ready" : "stub"}
          </div>
        </div>
      </details>
    </div>
  );
}
