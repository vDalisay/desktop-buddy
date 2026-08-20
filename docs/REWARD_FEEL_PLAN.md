# Reward Feel Plan (2026-08-20)

One popup, one entry point, three callers. Written after looking at how acquisition is sold in
modern games and throwing away everything that fights a small always-on-top Win98 window.

## What modern games actually do, and what survives the shell

| Technique | What it buys | Verdict here |
| --- | --- | --- |
| **Scale overshoot on entry** (Hades, Vampire Survivors, every mobile chest) | The single cheapest "punch". The eye reads an overshoot as impact even with no particles at all. | **Keep.** 0.18 s ease-out-back, overshoot ~6 %. Free, no assets. |
| **Anticipation beat / freeze before reveal** (CS:GO case, Overwatch loot box) | Builds tension, but only pays off when the *contents are unknown*. | **Cut.** The player just clicked "Buy Baseball Bat". Anticipation for a known item is dead air on a desktop toy that must not steal 2 s of your day. |
| **Card flip / 3D reveal** | Sells rarity tiers. | **Cut.** No rarity tiers exist, and a 3D flip inside a 2D Win98 chrome is exactly the "mobile game pasted into the shell" failure. |
| **Rarity glint / shine sweep** | Reads as "this is valuable". | **Kept in Win98 dialect only**: four static diagonal glints around the icon, drawn as hard pixels, not a gradient sweep. Gated on Reduced Particles. |
| **Radial glow behind the item** | Separates the icon from a busy background; makes a flat icon read as lit. | **Keep** — this is the owner's "glowy background". Built as one generated 64×64 radial-alpha texture drawn behind the icon; costs one texture and one draw. |
| **Particle burst / confetti** | Celebration. | **Cut.** At 260×190 logical px a burst either clips at the frame or spills over the buddy. The glints do the same job for ~0 cost. |
| **Breathing pulse while dwelling** | Keeps the eye on a thing that is *waiting* to be read. | **Keep** — owner's direction, and it reuses the tutorial spotlight's easing so the shell has one pulse idiom instead of two. |
| **Number count-up on the reward** | Makes a payout feel bigger than it is. | **Cut.** Rewards here are $5–$1000 and the shell's balance readout is authoritative; two numbers disagreeing for 400 ms is worse than no animation. |
| **Sound pairing (a "chunk" then a shimmer)** | Does more for feel than any visual. | **Already exists** — `UiSfx.Money` for purchases, `UiFeedbackCue.Reward` for milestones. The popup deliberately plays nothing itself; each caller keeps owning its own sound, matching the existing "Purchase and Equip sound themselves" rule in `ShopPanel`. |

The general lesson from the loot-reveal literature that *does* transfer: **the punch is in the
first 200 ms and the dwell is just reading time.** Everything expensive (particles, flips,
rarity beams) is a substitute for content the player hasn't earned yet. A desktop idler in a
grey window gets the same hit from overshoot + glow + a chunky icon.

## Timings and easings (as implemented)

| Phase | Duration | Curve | Why |
| --- | --- | --- | --- |
| In | 0.18 s | ease-out-back, scale 0.72 → 1.0 with ~1.06 overshoot, alpha 0 → 1 | Long enough to be seen at 60 Hz (11 frames), short enough that it never feels like a modal. |
| Dwell | 2.40 s | breathing scale 1.00 → 1.12, period 1.5 s, smoothstepped ping-pong | ~1.6 breaths. Reads as alive; a full sine crosses the mid-point fast and made the tutorial spotlight jitter sub-pixel — the same smoothstep-over-ping-pong fix from `TUTORIAL_CLOSURE_PLAN_2026-08-19.md` is reused verbatim. Panel size is rounded to whole pixels and the pulse scales about the panel centre, so bevels stay crisp. |
| Out | 0.14 s | ease-in, scale → 0.94, alpha 1 → 0 | Faster than the entry: leaving should not be an event. |
| Glow | continuous | alpha 0.55 ± 0.18, same 1.5 s ping-pong, in phase with the scale | One period for both, so it reads as one object breathing rather than two effects. |

Total on-screen time 2.72 s. A queued second reward starts immediately after the first
finishes (see Queue below).

## Accessibility (settings that already exist)

- **Reduced Motion** or **Modern UI Motion off** (`Win98MotionPolicy.Allows`): no entry
  overshoot, no exit shrink, **no breathing**. The popup simply appears at 100 %, dwells the
  same 2.40 s, and disappears. Motion is the only thing removed — the reward is still shown.
- **Photosensitivity Safe** (default **on**): the glow alpha stops oscillating and sits flat at
  0.55. Nothing on this popup modulates faster than 0.67 Hz even with it off, which is already
  far under any flicker threshold, but a *pulsing bright halo* is precisely what the setting is
  for so it is honoured rather than argued with.
- **Reduced Particles**: the four glints are not drawn.
- **Screen Shake**: nothing here moves rendered content off its own rect — no camera kick lane
  is used, so the setting is a no-op by construction rather than by check.
- **Interface Sounds**: the popup plays no audio at all. Callers use the existing
  `UiFeedbackAudioBootstrap` seam, which already routes to the interface bus and already
  respects the mix.

## Queue

Two rewards genuinely can land on one frame: `WorkCompanionCoordinator.DrainActivity` evaluates
the whole milestone catalogue on a single drain and a long typing burst can cross two thresholds
at once. So there is a plain `Queue<Request>` drained sequentially — about ten lines, no manager,
no priorities, no coalescing.

## Scope notes

- Pain payouts are deliberately **not** popped. They fire many times a minute and already have
  presentation in `MoneyHudPresenter`'s coalesced reward line; a middle-of-screen popup per hit
  would be the worst feature in the game.
- Icons are generated 16×16 Win98-palette pixel art scaled 4× with nearest filtering
  (`RewardIconProvider`), following the existing `PaintToolIconProvider` precedent: real artwork
  can later be dropped at `res://assets/ui/reward_icons/{slug}.svg` with no caller changes.

## Demo / mockup

```
<godot> --fixed-fps 120 --path . -- --scenario=reward_popup_demo --seed=1 --artifacts=.artifacts/reward_popup_demo
```

Run it **windowed** (no `--headless`) to get the frames; headless still runs the semantic
checks. It buys the most expensive tool through the real `ShopPanel` path and queues a Work
milestone and a lifetime achievement on the same frame, then writes three frames per reward
(entry, breath maximum, breath minimum) to the artifacts directory.

## Wiring

| Source | Call site |
| --- | --- |
| Purchase | `ShopPanel.Purchase`, beside the existing UiSfx.Money layer |
| Work milestone / lifetime achievement | `WorkCompanionCoordinator.AnnounceMilestone`, off the existing WorkMilestoneEarned list |
| Pain payouts | deliberately not wired - see Scope notes |
