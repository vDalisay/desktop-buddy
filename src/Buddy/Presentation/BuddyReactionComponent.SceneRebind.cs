using System;

namespace DesktopBuddy.Buddy.Presentation;

public partial class BuddyReactionComponent
{
    /// <summary>
    /// Clears only the outgoing live actor's acute face/fear timers. Persistent mood and harmful
    /// memory are resolved immediately from the newly rebound damage pipeline, so the target Buddy
    /// starts with its own semantic state rather than inheriting a punch, laugh or tickle beat.
    /// </summary>
    public void ResetForSceneRebind()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("BuddyReactionComponent cannot reset before initialization.");

        _painTicks = 0;
        _pistolSadTicks = 0;
        _delightTicks = 0;
        _fearTicks = 0;
        _petSmileTicks = 0;
        _learnedThreatFaceTicks = 0;
        _laughTicks = 0;
        _treatTicks = 0;
        _annoyedTickleTicks = 0;
        IsTickleAnnoyed = false;
        Resolve();
    }
}
