# Milestone 4 Owner Feel Gate

Automated prerequisites completed 2026-07-26: `dotnet test` is green at 611/611,
`tools\quick_validate.bat` passes all 15 checks, the valid headless scenario
matrix is 80/80, the valid journey matrix is 21/21, and full scenario/journey
soaks pass in both presentations. This review is windowed; do not run
`owner_feedback_visual` headless.

1. Launch `scenes/buddy_lab.tscn` windowed at the default Mii3D presentation.
   Move the cursor near and away from the buddy while using safe objects. Drive
   mood through Fearful, Wary, Neutral, Content, and Delighted using the lab
   controls/scenarios. Confirm all five read differently without a visible meter,
   especially the delegated standoff/approach and greeting cadences.
2. Consume the laboratory food through the real Eat choreography. Confirm care
   improves behavior/mood but grants no immediate money burst. Leave the app
   running and confirm its economic effect arrives only through passive income.
3. Earn damage money, change tool selection, and create harmful memory. Save &
   Quit, relaunch, and confirm balance, mood, memory, selection, and personality
   return while pose, loose objects, pain, knockout, grabs, and the lab-food
   cooldown do not. The buddy must start in an ordinary safe standing pose.
4. Hide to tray for at least 60 seconds. Confirm the window/rendering and ragdoll
   are frozen, then show it and confirm money/mood/time advanced without a physics
   burst or pose jump. On the reference machine, record hidden CPU; target `<0.5%`.
5. Recheck the accepted feel corrections: grab resistance walks rather than
   slides, panic-flail hands remain deterministic and match the accepted second
   attempt, and elastic overpull retains the five-hand-width cap, final-second
   buzz escalation, three-second snap, release, and peak-scaled fling.

Record the result in `docs/DECISIONS.md` only after hands-on completion. Until
then, Milestone 4 owner acceptance is explicitly **pending**.
