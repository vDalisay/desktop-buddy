using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Mood;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>M4 Task 3 gate for the data-driven five-band social vocabulary.</summary>
public sealed class MoodBandBehaviorScenario : IScenario
{
    public string Id => "mood_band_behavior";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("mood_band_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        (MoodBand Band, float Mood, float Distance, BehaviorPriority Owner, SocialStance Stance)[] cases =
        [
            (MoodBand.Fearful, -100.0f, 40.0f, BehaviorPriority.Social, SocialStance.Flee),
            (MoodBand.Wary, -40.0f, 40.0f, BehaviorPriority.Social, SocialStance.KeepDistance),
            (MoodBand.Neutral, 0.0f, 40.0f, BehaviorPriority.Ambient, SocialStance.None),
            (MoodBand.Content, 40.0f, 400.0f, BehaviorPriority.Social, SocialStance.Approach),
            (MoodBand.Delighted, 80.0f, 400.0f, BehaviorPriority.Social, SocialStance.Approach),
        ];

        bool vocabulary = true;
        var observed = new List<string>();
        foreach (var item in cases)
        {
            lab.Progress.ApplyCareMood(item.Mood - lab.Progress.Mood);
            lab.Buddy.Arbiter.Reset();
            Vector2 target = lab.Buddy.Rig.Torso.GlobalPosition +
                new Vector2(item.Distance, 0.0f);
            lab.Buddy.PhysicsTick(
                cursorWorldPosition: target,
                socialTargetValid: true);
            vocabulary &=
                lab.Progress.MoodBand == item.Band &&
                lab.Buddy.Arbiter.Intent.Owner == item.Owner &&
                lab.Buddy.Arbiter.Intent.Stance == item.Stance;
            observed.Add(
                $"{item.Band}:{lab.Buddy.Arbiter.Intent.Owner}/{lab.Buddy.Arbiter.Intent.Stance}");
        }

        // A content buddy standing next to the cursor must still live: greeting owns one
        // tick per cadence, and ambient autonomy owns the gaps. A greet that held
        // priority 6 for as long as the cursor stayed close froze the buddy in place.
        SocialTuningSet bands = lab.Buddy.Arbiter.SocialTuning;
        lab.Progress.ApplyCareMood(40.0f - lab.Progress.Mood);
        lab.Buddy.Arbiter.Reset();
        Vector2 near = lab.Buddy.Rig.Torso.GlobalPosition +
            new Vector2(bands.Content.ApproachDistance - 20.0f, 0.0f);
        lab.Buddy.PhysicsTick(cursorWorldPosition: near, socialTargetValid: true);
        bool greetsOnce = lab.Buddy.Arbiter.Intent.Owner == BehaviorPriority.Social &&
            lab.Buddy.Arbiter.Intent.GreetRequested;

        bool ambientKeepsRunning = true;
        for (int tick = 0; tick < 30; tick++)
        {
            near = lab.Buddy.Rig.Torso.GlobalPosition +
                new Vector2(bands.Content.ApproachDistance - 20.0f, 0.0f);
            lab.Buddy.PhysicsTick(cursorWorldPosition: near, socialTargetValid: true);
            ambientKeepsRunning &=
                lab.Buddy.Arbiter.Intent.Owner == BehaviorPriority.Ambient &&
                !lab.Buddy.Arbiter.Diagnostics.AmbientSuppressed;
        }

        checks.Add(new StartupCheck(
            "greeting_punctuates_without_freezing_ambient",
            greetsOnce && ambientKeepsRunning,
            $"greeted={greetsOnce} ambient_between={ambientKeepsRunning} " +
            $"owner={lab.Buddy.Arbiter.Intent.Owner}"));

        SocialTuningSet tuning = lab.Buddy.Arbiter.SocialTuning;
        bool catchGate =
            !tuning.Fearful.WillCatch &&
            !tuning.Wary.WillCatch &&
            !tuning.Neutral.WillCatch &&
            tuning.Content.WillCatch &&
            tuning.Delighted.WillCatch;
        bool cadence =
            tuning.Fearful.GreetIntervalTicks == 0 &&
            tuning.Wary.GreetIntervalTicks == 0 &&
            tuning.Neutral.GreetIntervalTicks == 0 &&
            tuning.Content.GreetIntervalTicks > tuning.Delighted.GreetIntervalTicks &&
            tuning.Delighted.GreetIntervalTicks > 0;

        checks.Add(new StartupCheck(
            "five_mood_bands_drive_distinct_social_stances",
            vocabulary,
            string.Join(" ", observed)));
        checks.Add(new StartupCheck(
            "mood_band_catch_gate_matches_vocabulary",
            catchGate,
            $"fearful={tuning.Fearful.WillCatch} wary={tuning.Wary.WillCatch} " +
            $"neutral={tuning.Neutral.WillCatch} content={tuning.Content.WillCatch} " +
            $"delighted={tuning.Delighted.WillCatch}"));
        checks.Add(new StartupCheck(
            "social_cadence_is_typed_and_band_specific",
            cadence,
            $"content={tuning.Content.GreetIntervalTicks} delighted={tuning.Delighted.GreetIntervalTicks}"));

        await M4ObjectScenarioSupport.Cleanup(tree, lab);
        bool passed = true;
        foreach (StartupCheck check in checks) passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}
