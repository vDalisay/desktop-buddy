export type SteamStatus = {
  available: boolean;
  reason: string;
};

const unlockedAchievements = new Set<string>();
const stats = new Map<string, number>();

export async function getSteamStatus(): Promise<SteamStatus> {
  return {
    available: false,
    reason: "Steamworks native binding is intentionally stubbed for the first vertical slice."
  };
}

export async function unlockAchievement(id: string): Promise<void> {
  unlockedAchievements.add(id);
  console.info(`[steam-stub] achievement unlocked: ${id}`);
}

export async function setStat(id: string, value: number): Promise<void> {
  stats.set(id, value);
  console.info(`[steam-stub] stat set: ${id}=${value}`);
}

