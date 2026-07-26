# Milestone 4 Owner Feel Gate

Automated prerequisites completed 2026-07-26 and re-verified after the review-fix
pass (`docs/M4_REVIEW_FIXES_PLAN.md`): `dotnet test` is green at 638/638,
`tools\quick_validate.bat` passes all 15 checks, the valid headless scenario matrix
is 78/78 (39 runnable scenarios in both presentations; the window-only visual gate
excluded and the 30-minute soak run as `idle_soak_ci`), and the valid journey matrix
is 21/21. This review is windowed; do not run `owner_feedback_visual` headless.

1. Launch `scenes/buddy_lab.tscn` windowed at the default Mii3D presentation.
   Move the cursor near and away from the buddy while using safe objects. Drive
   mood through Fearful, Wary, Neutral, Content, and Delighted using the lab
   controls/scenarios. Confirm all five read differently without a visible meter,
   especially the delegated standoff/approach and greeting cadences. A content or
   delighted buddy should keep ambling between waves rather than standing still
   near the cursor.
2. Drop safe objects on the floor in front of a walking buddy and watch it hop over
   them. The probe now fires just above the floor line, so obstacle-hop personality
   is live for the first time; confirm high-propensity saves hop and that hops still
   read as deliberate rather than random. Timer-driven ambient jumping stays off.
3. Consume the laboratory food through the real Eat choreography. Confirm care
   improves behavior/mood but grants no immediate money burst. Leave the app
   running and confirm its economic effect arrives only through passive income.
   Knock the food out of the buddy's hands mid-meal and confirm no cooldown starts.
4. Earn damage money, change tool selection, and create harmful memory. Save &
   Quit (`Ctrl+Shift+Q`), relaunch, and confirm balance, mood, memory, selection,
   and personality return while pose, loose objects, pain, knockout, grabs, and the
   lab-food cooldown do not. The buddy must start in an ordinary safe standing pose.
5. Hide to tray with `Ctrl+Shift+H` for at least 60 seconds. Confirm the window and
   ragdoll freeze and that rendering stops (the frame cap drops to `10` and the
   render loop is disabled). On the reference machine, record hidden CPU; target
   `<0.5%`.

   **Known M4 boundary:** *restoring* a hidden window is not reachable from the
   keyboard, because the OS delivers no input to an invisible unfocused window. The
   native tray icon and global hotkey that provide the restore stimulus are FR-016.1
   Milestone 6 scope. For this gate, verify the frozen-and-accruing half by
   observation plus the `hidden_clock_accrual` scenario, then relaunch. The show path
   itself (restore, no burst, no pose jump) is asserted automatically by that
   scenario in both presentations.
6. Recheck the accepted feel corrections: grab resistance walks rather than
   slides, panic-flail hands remain deterministic and match the accepted second
   attempt, and elastic overpull retains the five-hand-width cap, final-second
   buzz escalation, three-second snap, release, and peak-scaled fling.

Also outstanding as owner-manual, joining the M2 native Windows matrix rather than
this gate: real `WM_POWERBROADCAST` suspend/resume and session lock/unlock. The
lifecycle seam is wired and covered headless through the emulated adapter; the native
adapter declares the events and does not yet raise them.

Record the result in `docs/DECISIONS.md` only after hands-on completion. Until
then, Milestone 4 owner acceptance is explicitly **pending**.
