using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M5 Baseball Bat gate: the bat is the Boxing Glove mechanism with its own
/// authored shape, mass, tether, and content ID. A stationary hover must stay
/// below the pain floor, a real swing must score pain attributed to the bat, and
/// the harmful memory that follows must name the bat and never the glove.
/// The elongated collider must also hold square to its own swing, which is the
/// one behavior the round tools never needed.
/// </summary>
public sealed class BatSwingScenario : IScenario
{
    private const float SwingSpeed = 2400.0f;
    private const int HoverTicks = 120;

    public string Id => "bat_swing";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 500.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("bat_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        Vector2 approach = torso + new Vector2(-260.0f, 0.0f);
        lab.CursorTools.MoveCursor(approach);
        for (int tick = 0; tick < 20; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        CursorToolBody? bat = lab.CursorTools.Body;
        checks.Add(new StartupCheck(
            "selecting_the_bat_spawns_its_own_elongated_collider",
            lab.Progress.IsToolUnlocked(ContentIds.ToolBaseballBat) &&
            lab.CursorTools.IsActive &&
            bat is not null &&
            bat.ContentId == ContentIds.ToolBaseballBat &&
            bat.IsElongated &&
            lab.CursorTools.ActiveProfile!.ContentId == ContentIds.ToolBaseballBat,
            $"owned={lab.Progress.IsToolUnlocked(ContentIds.ToolBaseballBat)} " +
            $"active={lab.CursorTools.IsActive} content={bat?.ContentId} " +
            $"length={bat?.Length} radius={bat?.Radius}"));
        if (bat is null)
        {
            return Finish(checks, messages, lab, tree);
        }

        // A hover is the tool resting in contact with no swing behind it. The
        // approach that gets it there is allowed to land a real hit — that is a
        // swing, however gentle. What must not happen is the resting bat scoring
        // again and again off solver jitter, which is what the episode re-arm
        // window and the curve floor exist to prevent.
        // The contact itself nudges the buddy along, so the hover anchor is measured
        // from where the torso is now, not where it started.
        Vector2 RestingTouch() => lab.Buddy.Rig.Torso.GlobalPosition + new Vector2(
            -(lab.Buddy.Rig.Torso.Radius + bat.Radius + 1.0f), 0.0f);

        Vector2 creep = approach;
        for (int tick = 0; tick < 400 && NearestSurfaceGap(lab, bat) > 0.5f; tick++)
        {
            creep = creep.MoveToward(RestingTouch(), 1.5f);
            lab.CursorTools.MoveCursor(creep);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        // The anchor is pinned where the creep ended. Chasing the torso instead would
        // make the tether shove the buddy across the room, which is a slow swing, not
        // a hover — and it is the buddy moving away that would make this vacuous.
        Vector2 hoverAnchor = creep;
        for (int tick = 0; tick < 60; tick++)
        {
            lab.CursorTools.MoveCursor(hoverAnchor);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        long hoverEpisodes = 0;
        long hoverScored = 0;
        void OnHoverEpisode(AcceptedContactEpisode episode)
        {
            if (episode.ContentId == ContentIds.ToolBaseballBat)
                hoverEpisodes++;
        }
        void OnHoverImpact(AcceptedImpact impact)
        {
            if (impact.ContentId == ContentIds.ToolBaseballBat)
                hoverScored++;
        }
        lab.Pipeline.EpisodeAccepted += OnHoverEpisode;
        lab.Pipeline.ImpactAccepted += OnHoverImpact;

        // Proving the hover is a hover: without a bat parked against the buddy this
        // check would pass vacuously, which is exactly how a dead assertion hides for
        // a milestone. The settling contact pushes the buddy a few pixels clear and
        // the solver will not hold a penetration, so "resting against" is a small gap
        // that stays small — not a permanent overlap.
        float widestHoverGap = float.NegativeInfinity;
        float narrowestHoverGap = float.PositiveInfinity;
        for (int tick = 0; tick < HoverTicks; tick++)
        {
            lab.CursorTools.MoveCursor(hoverAnchor);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            float gap = NearestSurfaceGap(lab, bat);
            widestHoverGap = Mathf.Max(widestHoverGap, gap);
            narrowestHoverGap = Mathf.Min(narrowestHoverGap, gap);
        }

        lab.Pipeline.EpisodeAccepted -= OnHoverEpisode;
        lab.Pipeline.ImpactAccepted -= OnHoverImpact;
        bool armedForHover = bat.IsImpactArmed;
        checks.Add(new StartupCheck(
            "stationary_hover_scores_no_pain",
            armedForHover &&
            narrowestHoverGap <= 6.0f &&
            widestHoverGap <= 16.0f &&
            hoverScored == 0L,
            $"armed={armedForHover} narrowest_gap={narrowestHoverGap:F2}px " +
            $"widest_gap={widestHoverGap:F2}px scored={hoverScored} " +
            $"episodes={hoverEpisodes} over {HoverTicks} ticks"));

        // A real swing: the cursor crosses the buddy at speed, so the impulse the
        // solver measures is the bat's own momentum rather than an authored number.
        AcceptedImpact? batImpact = null;
        void OnImpact(AcceptedImpact impact)
        {
            if (batImpact is null && impact.ContentId == ContentIds.ToolBaseballBat)
                batImpact = impact;
        }
        lab.Pipeline.ImpactAccepted += OnImpact;

        float alignmentErrorDegrees = float.PositiveInfinity;
        Vector2 windUp = torso + new Vector2(-300.0f, 0.0f);
        lab.CursorTools.MoveCursor(windUp);
        for (int tick = 0; tick < 30; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        float step = SwingSpeed / Engine.PhysicsTicksPerSecond;
        Vector2 swingPoint = windUp;
        for (int tick = 0; tick < 60 && batImpact is null; tick++)
        {
            swingPoint += new Vector2(step, 0.0f);
            lab.CursorTools.MoveCursor(swingPoint);
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            // Sampled mid-swing, before contact deflects the barrel: a bat swung
            // along X must present its length along Y.
            if (batImpact is null && GodotObject.IsInstanceValid(bat))
            {
                float longAxis = bat.GlobalRotation + (Mathf.Pi * 0.5f);
                float error = Mathf.Abs(
                    AlignmentErrorDegrees(longAxis, Mathf.Pi * 0.5f));
                alignmentErrorDegrees = Mathf.Min(alignmentErrorDegrees, error);
            }
        }

        for (int tick = 0; tick < 30 && batImpact is null; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        lab.Pipeline.ImpactAccepted -= OnImpact;

        checks.Add(new StartupCheck(
            "fast_swing_scores_pain_attributed_to_the_bat",
            batImpact is { Pain: > 0.0f } hit &&
            hit.ContentId == ContentIds.ToolBaseballBat &&
            hit.MilliCredits > 0L,
            $"content={batImpact?.ContentId} impulse={batImpact?.Impulse:F1} " +
            $"pain={batImpact?.Pain:F2} milli={batImpact?.MilliCredits} " +
            $"part={batImpact?.Part}"));

        checks.Add(new StartupCheck(
            "swung_bat_holds_square_to_its_travel",
            alignmentErrorDegrees <= 20.0f,
            $"best_alignment_error={alignmentErrorDegrees:F2}deg"));

        // The buddy learns the bat specifically. Attribution that leaked into the
        // glove would make it flinch from a tool that never touched it.
        checks.Add(new StartupCheck(
            "harmful_history_records_the_bat_and_not_the_glove",
            lab.Progress.IsContentHarmful(ContentIds.ToolBaseballBat) &&
            !lab.Progress.IsContentHarmful(ContentIds.ToolBoxingGlove),
            $"bat={lab.Progress.IsContentHarmful(ContentIds.ToolBaseballBat)} " +
            $"glove={lab.Progress.IsContentHarmful(ContentIds.ToolBoxingGlove)}"));

        // Statistics key on the same content ID the impact carried (M5 stats seam).
        // Only the pain half is asserted: ProgressStatistics.ToolUses has no runtime
        // writer yet — BuddyProgressState.RecordContentUse is unreferenced — and what
        // counts as one "use" of a swung, fired, or thrown tool is an owner call, not
        // one to guess inside a slice.
        ProgressStatistics statistics = lab.Progress.Statistics;
        long batPain = CountFor(statistics.ToolPainMilli, ContentIds.ToolBaseballBat);
        long glovePain = CountFor(statistics.ToolPainMilli, ContentIds.ToolBoxingGlove);
        checks.Add(new StartupCheck(
            "statistics_credit_the_bat_by_content_id",
            batPain > 0L && glovePain == 0L,
            $"bat_pain_milli={batPain} glove_pain_milli={glovePain}"));

        // Selecting a different cursor tool must hand the mechanism over cleanly:
        // one collider at a time, and the new one carries its own identity.
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        lab.CursorTools.MoveCursor(torso + new Vector2(-260.0f, 0.0f));
        for (int tick = 0; tick < 6; tick++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        CursorToolBody? glove = lab.CursorTools.Body;
        checks.Add(new StartupCheck(
            "swapping_tools_replaces_the_collider_and_its_identity",
            !GodotObject.IsInstanceValid(bat) &&
            glove is not null &&
            glove.ContentId == ContentIds.ToolBoxingGlove &&
            !glove.IsElongated,
            $"bat_freed={!GodotObject.IsInstanceValid(bat)} content={glove?.ContentId} " +
            $"elongated={glove?.IsElongated}"));

        messages.Add(
            $"pain={batImpact?.Pain:F2} impulse={batImpact?.Impulse:F1} " +
            $"alignment_error={alignmentErrorDegrees:F2}deg hover_ticks={HoverTicks}");
        return Finish(checks, messages, lab, tree);
    }

    private static long CountFor(IReadOnlyDictionary<string, long>? counters, string contentId) =>
        counters is not null && counters.TryGetValue(contentId, out long value) ? value : 0L;

    /// <summary>
    /// Gap between the nearest buddy part's surface and the bat's, measuring the
    /// capsule as the segment it really is rather than as a point at its center —
    /// a 90 px bat held sideways is nowhere near where its origin says it is.
    /// </summary>
    private static float NearestSurfaceGap(BuddyLab lab, CursorToolBody bat)
    {
        float halfShaft = Mathf.Max(0.0f, (bat.Length * 0.5f) - bat.Radius);
        Vector2 axis = Vector2.Down.Rotated(bat.GlobalRotation);
        Vector2 start = bat.GlobalPosition - (axis * halfShaft);
        Vector2 end = bat.GlobalPosition + (axis * halfShaft);
        float nearest = float.PositiveInfinity;
        foreach (var part in lab.Buddy.Rig.Parts)
        {
            Vector2 closest = Geometry2D.GetClosestPointToSegment(
                part.GlobalPosition, start, end);
            float gap = part.GlobalPosition.DistanceTo(closest) - part.Radius - bat.Radius;
            nearest = Mathf.Min(nearest, gap);
        }

        return nearest;
    }

    /// <summary>
    /// Signed degrees between two axes of a two-ended tool, where a half turn is
    /// the same axis — the bat has no preferred end.
    /// </summary>
    private static float AlignmentErrorDegrees(float axis, float target)
    {
        float error = Mathf.AngleDifference(axis, target);
        if (error > Mathf.Pi * 0.5f)
            error -= Mathf.Pi;
        else if (error < -Mathf.Pi * 0.5f)
            error += Mathf.Pi;
        return Mathf.RadToDeg(error);
    }

    private static ScenarioResult Finish(
        List<StartupCheck> checks,
        List<string> messages,
        BuddyLab lab,
        SceneTree tree)
    {
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
