import { Canvas } from "@react-three/fiber";
import { Suspense, useEffect } from "react";
import { Scene } from "./scene/Scene";
import { GameHud } from "./ui/GameHud";
import { useGameStore } from "./state/gameStore";

function Bootstrap(): null {
  const setSettings = useGameStore((state) => state.setSettings);
  const addCurrency = useGameStore((state) => state.addCurrency);
  const setSteamAvailable = useGameStore((state) => state.setSteamAvailable);
  const toSaveData = useGameStore((state) => state.toSaveData);

  useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      const [save, steam] = await Promise.all([window.desktopBuddy.save.read(), window.desktopBuddy.steam.status()]);
      if (cancelled) return;
      setSettings(save.settings);
      addCurrency(save.currency);
      setSteamAvailable(steam.available);
      await window.desktopBuddy.steam.setStat("currency_seen", save.currency);
    }

    void load();
    const unsubscribeSettings = window.desktopBuddy.settings.onChanged(setSettings);

    const interval = window.setInterval(() => {
      addCurrency(1);
      void window.desktopBuddy.save.write(toSaveData());
    }, 5000);

    return () => {
      cancelled = true;
      unsubscribeSettings();
      window.clearInterval(interval);
      void window.desktopBuddy.save.write(toSaveData());
    };
  }, [addCurrency, setSettings, setSteamAvailable, toSaveData]);

  return null;
}

export function GameRoot() {
  return (
    <main className="game-root">
      <Bootstrap />
      <Canvas shadows gl={{ alpha: true, antialias: true }} dpr={[1, 2]}>
        <Suspense fallback={null}>
          <Scene />
        </Suspense>
      </Canvas>
      <GameHud />
    </main>
  );
}
