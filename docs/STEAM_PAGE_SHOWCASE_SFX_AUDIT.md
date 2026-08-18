# Steam page showcase SFX audit

Status: **engineering hooks ready; owner replacement assets still pending**  
Branch: `agent/steam-page-gameplay-polish`  
Recorded: 2026-08-18

This document is the handoff list for the sounds used by the Steam-page capture systems. It records the gameplay event that owns each cue, the replacement slot that already exists, and what audio the owner still needs to supply. Audio presentation must remain downstream of gameplay authority: replacing a stream must never change damage, fuse state, purchases, paint state, or Work Mode reward settlement.

## Pistol

Owner-facing replacement slots live on `ReactionAudioPresenter`:

- `PistolShot1`
- `PistolShot2`
- `PistolReload`

Runtime events:

- `CursorGunComponent.ShotFired` -> pistol shot variation
- `CursorGunComponent.ReloadStarted` -> reload cue

Engineering state:

- shot/reload event routing is implemented;
- the presenter uses an 8-voice SFX pool, so the faster capture cadence can overlap transients instead of cutting every previous shot short;
- shot and reload counters remain deterministic test oracles;
- this branch does not commit final owner-authored pistol audio assets.

Owner asset request:

1. two short pistol-shot variants with similar perceived loudness;
2. one reload/magazine cue.

Optional later cue: dry-fire. It is not required for the current Steam capture acceptance gate.

## Baseball Bat

Replacement slots live on `SwingAudioComponent`:

- `ChargeStartedStream`
- `ChargeCompletedStream`
- `SwingReleasedStream`
- `HomeRunImpactStream`

Runtime events:

- charge start;
- full-charge completion;
- swing release;
- accepted charged-swing impact.

Engineering state:

- all four semantics are separated and replacement-ready;
- deterministic synthesized cues remain as fallback so missing owner files never break gameplay;
- the component routes through the SFX bus and does not alter charge or impact authority.

Owner asset request:

1. subtle charge-start cue;
2. clear full-charge-ready cue;
3. swing/whoosh cue;
4. heavier home-run impact cue.

## Boxing Glove

Replacement slots live on `ReactionAudioPresenter`:

- `GloveImpact1`
- `GloveImpact2`
- `GloveImpact3`
- `GloveImpact4`
- `GloveCriticalHeadImpact`

Runtime event:

- `InteractionDamageComponent.ImpactAccepted`.

Engineering state:

- ordinary glove hits use the glove variation set when supplied;
- a hard head hit uses the dedicated critical-head slot when supplied and otherwise falls back to the normal glove/general impact path;
- critical audio observes the same head/impulse condition as the short glove hitlag and does not create extra damage.

Owner asset request:

1. 2-4 short padded punch/body-impact variants;
2. one sharper/heavier critical-head accent.

## Grenade

Replacement slots live on `GrenadeAudioComponent`:

- `BoomStream`
- `ThudStream`
- `PinPullStream`
- `FuseLoopStream`

Runtime events/state:

- `GrenadeComponent.Detonated` -> boom;
- `GrenadeComponent.GroundContact` -> thud;
- `GrenadeComponent.PinPulled` -> pin pull;
- any live tracked fuse -> dedicated fuse loop lane.

Engineering state:

- boom/thud/pin cues use polyphonic punctuation playback;
- fuse ambience owns a separate loop player, so one detonation cannot truncate another grenade's countdown;
- clean-room synthesized fallback cues remain until owner files are assigned;
- simultaneous-grenade tests assert polyphony and continued fuse audio after the first staggered detonation.

Owner asset request:

1. explosion;
2. grenade body thud/landing;
3. mechanical pin pull;
4. short seamless fuse hiss/crackle loop.

## Paint Buddy

Replacement observation seam:

- `PaintCanvasControl.PaintAudioSampled` emitting `PaintAudioSample`.

Engineering state:

- samples are emitted only from accepted, rate-limited paint activity rather than every raw mouse-motion event;
- the same bounded successful-paint cadence drives the cosmetic droplet effect;
- the current branch deliberately does not add a final paint sound or a raw per-pixel audio player.

Owner asset request:

Choose one of these approaches after hearing the candidate asset:

- **loop approach:** one quiet brush/paint loop while accepted paint samples continue, with short fade in/out;
- **dab approach:** 2-4 very short paint dabs selected at the bounded sample cadence.

Do not wire a sound to every input sample; that would become noisy and needlessly expensive at high pointer rates.

## Buddy Studio

Current audio authority:

- standard Win98/UI feedback remains on the existing UI feedback path;
- capture polish adds visual purchase/equip acknowledgement through the shared Win98 motion helper, without creating a second commerce event.

Owner asset request:

- no dedicated new capture asset is required unless the existing Buy/Equip/Save feedback is judged weak during the owner pass.

If a custom purchase cue is added later, it should subscribe to the successful transaction/commit path and replace or suppress duplicate generic confirmation audio rather than playing both.

## Work Mode

Replacement observation seam:

- `WorkCompanionView.RewardPulseRequested(long milliCredits)` fires only after settled session credits increase.

Engineering state:

- visual reward feedback reads already-settled credits;
- the event cannot mutate the wallet and therefore cannot duplicate payouts;
- existing Work Mode audio/mute behavior remains authoritative.

Owner asset request:

- optional short reward/credit-earned UI cue for milestone payout.

This cue is helpful for final polish but not required to understand the capture because the CRT-adjacent `EARNED` readout and payout pulse are already visible.

## Capture replacement checklist

Before final recording, assign/listen to owner audio in this order:

1. pistol shots + reload;
2. grenade boom + fuse + pin + thud;
3. baseball bat swing/home-run/charge cues;
4. boxing glove normal + critical hit;
5. Paint Buddy stroke/dab sound;
6. optional Work reward cue;
7. recheck Buddy Studio generic UI confirmation for duplication.

For every replacement pass verify:

- no cue changes gameplay state;
- SFX and UI buses still obey their existing volume controls;
- rapid pistol/grenade events overlap cleanly;
- Work mute behavior remains respected;
- missing assets degrade to fallback/silence instead of blocking the game.
