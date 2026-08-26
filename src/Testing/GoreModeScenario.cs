using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Gore Mode's two gates, its bleeding, and its wiring.
///
/// <para>It runs against <c>sandbox.tscn</c> rather than the laboratory on purpose. The
/// grenade slice shipped its reactions wired into <see cref="BuddyLab"/> only, so every
/// rule passed in a scenario and none of them worked in the game; Gore Mode is wired the
/// other way round, and <c>gore_is_wired_into_the_shipped_root</c> is the standing guard
/// that it stays that way.</para>
///
/// <para>Wounds are opened through <see cref="InteractionDamageComponent.ApplyBlastImpulse"/>
/// because it is the one public entry that publishes a fully scored impact under a chosen
/// content ID. That exercises the real listener path — the same event a bullet raises —
/// without having to aim a gun, which is bounded-rate pursuit and cannot be done by
/// teleporting a cursor.</para>
/// </summary>
public sealed class GoreModeScenario : IScenario
{
    public string Id => "gore_mode";

    /// <summary>Comfortably over the pain floor a wound needs, through the shared curve.</summary>
    private const float WoundingImpulse = 2600.0f;

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("sandbox_scene_loadable", false, "res://scenes/sandbox.tscn"));
            return new ScenarioResult(false, checks, messages);
        }

        Node instance = packed.Instantiate();
        tree.Root.AddChild(instance);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        if (instance is not SandboxRoot sandbox)
        {
            checks.Add(new StartupCheck("sandbox_composed", false, instance.GetType().Name));
            instance.QueueFree();
            return new ScenarioResult(false, checks, messages);
        }

        bool? restoreGore = DemoScope.GoreOverride;
        try
        {
            await Run(tree, sandbox, checks);
        }
        finally
        {
            DemoScope.GoreOverride = restoreGore;
            instance.QueueFree();
        }

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;

        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task Run(SceneTree tree, SandboxRoot sandbox, List<StartupCheck> checks)
    {
        GoreComponent gore = sandbox.Gore;
        bool wired = GodotObject.IsInstanceValid(gore) && gore.IsInitialized;

        checks.Add(new StartupCheck(
            "gore_is_wired_into_the_shipped_root",
            wired,
            $"valid={GodotObject.IsInstanceValid(gore)} wired={wired}"));

        // Every check below reads off the component; without it there is nothing to assert
        // and going on would report a cascade of failures that all mean this one thing.
        if (!wired)
            return;

        CursorToolProfile? sword = SwordProfile(sandbox);
        checks.Add(new StartupCheck(
            "the_sword_is_an_authored_cursor_tool",
            sword is not null,
            $"profiles={(sandbox.CursorTools.Profiles is { } authored ? authored.Count : 0)}"));

        // The Sword is wielded, not charged and swung (owner instruction 2026-08-25). Both
        // halves matter: authoring a Swing back onto it would silently restore the charged
        // bat behaviour, and the two cannot coexist because they want the same button.
        checks.Add(new StartupCheck(
            "the_sword_is_wielded_point_first_rather_than_swung",
            sword is { IsThrustCapable: true, IsSwingCapable: false, IsPunchCapable: false },
            $"thrust={sword?.IsThrustCapable} swing={sword?.IsSwingCapable} punch={sword?.IsPunchCapable}"));

        checks.Add(new StartupCheck(
            "the_sword_profile_is_valid",
            sword is not null && sword.Validate().Count == 0,
            sword is null ? "no profile" : string.Join("; ", sword.Validate())));

        // --- the build gate: an untagged storefront build refuses gore outright ---
        DemoScope.GoreOverride = false;
        sandbox.ApplyEffectsSettings(EffectsSettings.Default with { Gore = true });
        int before = gore.WoundsOpened;
        Wound(sandbox);
        await Ticks(tree, 2);
        checks.Add(new StartupCheck(
            "a_build_without_the_feature_refuses_gore_even_when_the_setting_says_yes",
            !gore.IsActive && gore.WoundsOpened == before && !gore.IsBleeding,
            $"active={gore.IsActive} wounds={gore.WoundsOpened} (was {before})"));

        // --- the player gate: the feature ships, the setting is off ---
        DemoScope.GoreOverride = true;
        sandbox.ApplyEffectsSettings(EffectsSettings.Default with { Gore = false });
        before = gore.WoundsOpened;
        Wound(sandbox);
        await Ticks(tree, 2);
        checks.Add(new StartupCheck(
            "gore_off_draws_no_blood",
            !gore.IsActive && gore.WoundsOpened == before && !gore.IsBleeding,
            $"active={gore.IsActive} wounds={gore.WoundsOpened} (was {before})"));

        // --- both gates open ---
        sandbox.ApplyEffectsSettings(EffectsSettings.Default with { Gore = true });
        before = gore.WoundsOpened;
        Wound(sandbox);
        await Ticks(tree, 2);
        bool opened = gore.WoundsOpened == before + 1 && gore.IsBleeding;
        checks.Add(new StartupCheck(
            "a_piercing_hit_opens_a_bleeding_wound",
            opened,
            $"wounds={gore.WoundsOpened} bleeding={gore.IsBleeding} " +
            $"remaining={gore.WoundOn(BuddyPart.Head).TicksRemaining}"));

        // --- the guns bleed him too ---
        // pistol_fire proves the pipeline publishes a real bullet hit as tool.pistol with
        // pain 53.8; this proves Gore Mode wounds on exactly that. Together they cover the
        // whole path without a scenario having to aim a gun, which is bounded-rate pursuit
        // and cannot be done by teleporting a cursor.
        foreach (string gun in new[] { ContentIds.ToolPistol, ContentIds.ToolShotgun })
        {
            gore.ClearAll();
            before = gore.WoundsOpened;
            sandbox.Pipeline.ApplyBlastImpulse(
                InteractionIds.Next(),
                gun,
                BuddyPart.Torso,
                WoundingImpulse,
                sandbox.Buddy.Rig.Torso.GlobalPosition);
            await Ticks(tree, 2);
            checks.Add(new StartupCheck(
                $"a_hit_from_{gun.Replace("tool.", string.Empty)}_opens_a_wound",
                gore.WoundsOpened == before + 1 && gore.WoundOn(BuddyPart.Torso).IsBleeding,
                $"wounds={gore.WoundsOpened} (was {before})"));
        }

        // --- blunt and toy weapons never draw blood, however hard they land ---
        foreach (string blunt in new[] { ContentIds.ToolBaseballBat, ContentIds.ToolNerfBlaster })
        {
            gore.ClearAll();
            before = gore.WoundsOpened;
            sandbox.Pipeline.ApplyBlastImpulse(
                InteractionIds.Next(),
                blunt,
                BuddyPart.Torso,
                WoundingImpulse,
                sandbox.Buddy.Rig.Torso.GlobalPosition);
            await Ticks(tree, 2);
            checks.Add(new StartupCheck(
                $"{blunt.Replace("tool.", string.Empty)}_draws_no_blood",
                gore.WoundsOpened == before && !gore.WoundOn(BuddyPart.Torso).IsBleeding,
                $"wounds={gore.WoundsOpened} (was {before})"));
        }

        // Back to a bleeding head for the drip checks below.
        Wound(sandbox);
        await Ticks(tree, 2);

        // --- an open wound drips, and what lands stains ---
        int drips = gore.DripsEmitted;
        int stains = gore.Stains.TotalStainsAdded;
        await Ticks(tree, 900);
        checks.Add(new StartupCheck(
            "an_open_wound_keeps_dripping",
            gore.DripsEmitted > drips,
            $"drips={gore.DripsEmitted} (was {drips})"));
        checks.Add(new StartupCheck(
            "the_opening_hit_stained_the_buddy",
            gore.Stains.TotalStainsAdded > stains,
            $"stains={gore.Stains.TotalStainsAdded} (was {stains})"));

        // The owner's report: blood "seems to infinitely stay". Live stains are bounded by
        // the caps, by merging into the pools already there, and by drying out — so a long
        // bleed adds far more stains than it is ever holding at once.
        checks.Add(new StartupCheck(
            "a_long_bleed_does_not_grow_the_stain_set_without_bound",
            gore.Stains.StainCount <= 100 &&
            gore.Stains.TotalStainsAdded > gore.Stains.StainCount,
            $"live={gore.Stains.StainCount} added={gore.Stains.TotalStainsAdded}"));

        // --- patching him up closes every wound and wipes every mark ---
        gore.ClearAll();
        checks.Add(new StartupCheck(
            "clearing_closes_the_wounds_and_wipes_the_stains",
            !gore.IsBleeding && gore.Stains.StainCount == 0,
            $"bleeding={gore.IsBleeding} stains={gore.Stains.StainCount}"));

        // --- turning the setting off must leave nothing on screen ---
        Wound(sandbox);
        await Ticks(tree, 2);
        sandbox.ApplyEffectsSettings(EffectsSettings.Default with { Gore = false });
        checks.Add(new StartupCheck(
            "switching_gore_off_stops_the_bleeding_and_clears_what_was_there",
            !gore.IsBleeding && gore.Stains.StainCount == 0,
            $"bleeding={gore.IsBleeding} stains={gore.Stains.StainCount}"));
    }

    private static void Wound(SandboxRoot sandbox) =>
        sandbox.Pipeline.ApplyBlastImpulse(
            InteractionIds.Next(),
            ContentIds.ToolSword,
            BuddyPart.Head,
            WoundingImpulse,
            sandbox.Buddy.Rig.GetPart(Buddy.Physics.BuddyPartId.Head).GlobalPosition);

    private static CursorToolProfile? SwordProfile(SandboxRoot sandbox)
    {
        Godot.Collections.Array<CursorToolProfile>? profiles = sandbox.CursorTools.Profiles;
        if (profiles is null)
            return null;

        foreach (CursorToolProfile profile in profiles)
        {
            if (GodotObject.IsInstanceValid(profile) && profile.ContentId == ContentIds.ToolSword)
                return profile;
        }

        return null;
    }

    private static async Task Ticks(SceneTree tree, int ticks)
    {
        for (int index = 0; index < ticks; index++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }
}
