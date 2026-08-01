using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Objects;

/// <summary>
/// Immutable authored metadata and physical tuning for one loose-object kind.
/// Runtime ownership, throw tokens, hold/protection state, and rest tracking live
/// in <see cref="LooseObjectRegistry"/>, never in this Resource.
/// </summary>
[GlobalClass]
public partial class LooseObjectProfile : GameResource
{
    [Export] public string ContentId { get; set; } = ContentIds.LooseObject;
    [Export] public bool Consumable { get; set; }

    /// <summary>
    /// Mood granted when the buddy finishes eating this (FR-008.4). Authored per consumable —
    /// the Meal, Drink, and Repair Kit differ only in this data, not in machinery.
    /// </summary>
    [Export(PropertyHint.Range, "0,100,0.5")] public float ConsumeMoodGain { get; set; } = 10.0f;

    /// <summary>
    /// Reuse cooldown in routed ticks, started only by a successful consume (FR-008.10).
    /// <c>7200</c> is 60 s at 120 Hz. Food leaves this at <c>0</c>: appetite, not a timer,
    /// decides whether the buddy eats (owner decision 2026-07-29).
    /// </summary>
    [Export(PropertyHint.Range, "0,72000,1")] public int ConsumeCooldownTicks { get; set; }

    /// <summary>
    /// How many points of the <c>200</c>-point hunger bar this item fills. The buddy accepts
    /// it only when it fits in the room left, so portion size is the whole decision: a nearly
    /// full buddy takes a snack and refuses a banquet. <c>0</c> means "not food" — a
    /// consumable that is never refused for appetite.
    /// </summary>
    [Export(PropertyHint.Range, "0,200,1")] public float ConsumeHungerFill { get; set; }
    /// <summary>
    /// How this consumable is taken. The Meal keeps the repeated-bite gesture; the Drink is
    /// raised to the head once and held there (owner instruction 2026-08-01). Authored rather
    /// than inferred, so a future consumable picks a gesture instead of a special case.
    /// </summary>
    [Export] public ConsumeGestureStyle ConsumeStyle { get; set; } = ConsumeGestureStyle.Bites;

    /// <summary>Routed ticks the single raise takes to reach the head. Ignored by the bites style.</summary>
    [Export(PropertyHint.Range, "6,600,1")] public int ConsumeRaiseTicks { get; set; } = 60;

    /// <summary>Routed ticks it is held at the head before it is gone. <c>240</c> is two seconds.</summary>
    [Export(PropertyHint.Range, "6,1800,1")] public int ConsumeHoldTicks { get; set; } = 240;

    [Export] public bool Hazardous { get; set; }
    [Export] public bool SafeToEvict { get; set; } = true;

    [Export(PropertyHint.Range, "1,128,0.5")] public float Radius { get; set; } = 12.0f;
    [Export(PropertyHint.Range, "0.01,100,0.01")] public float Mass { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,100,0.01")] public float LinearDamp { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "0,100,0.01")] public float AngularDamp { get; set; } = 2.0f;
    /// <summary>
    /// Restitution, applied through a <see cref="PhysicsMaterial"/> when the body takes this
    /// profile. <c>0</c> is Godot's own default — a dead thud — so every profile authored
    /// before this field existed keeps its measured behavior exactly. A Soccer Ball that does
    /// not bounce is only a big Baseball, which is why bounce is authored rather than shared.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Bounce { get; set; }

    [Export(PropertyHint.Range, "0,100,0.1")] public float RestSpeedThreshold { get; set; } = 5.0f;
    [Export(PropertyHint.Range, "1,600,1")] public int RestTicksRequired { get; set; } = 60;
    [Export] public Color FillColor { get; set; } = new("ffd27a");
    [Export] public Color OutlineColor { get; set; } = new("183042");

    /// <summary>
    /// Which drawn shape this object takes in the Mii3D presentation.
    /// <see cref="LooseObjectVisualKind.None"/> -- the case for every object authored before
    /// the Soccer Ball -- keeps the flat circle it has always had, in both modes.
    /// </summary>
    [Export] public LooseObjectVisualKind Visual3D { get; set; } = LooseObjectVisualKind.None;

    /// <summary>How far in front of the room plane the drawn mesh sits.</summary>
    [Export(PropertyHint.Range, "-64,64,0.5")] public float VisualDepthOffset { get; set; } = 6.0f;

    /// <summary>
    /// Optional pullback tuning used when the player launches <i>this</i> item.
    /// <c>null</c> — the case for every launchable authored before the Soccer Ball — means the
    /// launcher's own shared preset, so nothing that did not author one changes. Authored
    /// rather than shared because a playground ball should leave the hand slower and loopier
    /// than a baseball, and that is a per-item feel number.
    /// </summary>
    [Export] public Tools.PullbackLauncherProfile? Launch { get; set; }

    /// <summary>
    /// Optional opt-in to the trap → dwell → kick beat (owner instruction 2026-08-01). Only the
    /// Soccer Ball authors one; every other loose object leaves this <c>null</c> and is never
    /// even read into a <see cref="DesktopBuddy.Domain.Autonomy.SoccerBallReading"/>.
    /// </summary>
    [Export] public SoccerPlayProfile? SoccerPlay { get; set; }

    public bool IsRuntimeValid =>
        !string.IsNullOrWhiteSpace(ContentId) &&
        float.IsFinite(Radius) && Radius > 0.0f &&
        float.IsFinite(Mass) && Mass > 0.0f &&
        float.IsFinite(LinearDamp) && LinearDamp >= 0.0f &&
        float.IsFinite(AngularDamp) && AngularDamp >= 0.0f &&
        float.IsFinite(Bounce) && Bounce >= 0.0f && Bounce <= 1.0f &&
        float.IsFinite(RestSpeedThreshold) && RestSpeedThreshold >= 0.0f &&
        RestTicksRequired > 0 &&
        (Launch is null || (GodotObject.IsInstanceValid(Launch) && Launch.IsRuntimeValid)) &&
        (SoccerPlay is null ||
         (GodotObject.IsInstanceValid(SoccerPlay) && SoccerPlay.IsRuntimeValid)) &&
        !(Hazardous && SafeToEvict) &&
        (!Consumable ||
         (ConsumeRaiseTicks > 0 && ConsumeHoldTicks > 0 &&
          float.IsFinite(ConsumeMoodGain) && ConsumeMoodGain > 0.0f &&
          ConsumeCooldownTicks >= 0 &&
          float.IsFinite(ConsumeHungerFill) && ConsumeHungerFill >= 0.0f));

    /// <summary>
    /// The approved consume tuning for this item. The cooldown/one-success rule itself lives
    /// in <see cref="CareConsumableModel"/>; this Resource only says how much and how long.
    /// </summary>
    public CareConsumableTuning ToConsumableTuning() =>
        new(ConsumeMoodGain, ConsumeCooldownTicks);

    /// <summary>
    /// The gesture this item is taken with. <paramref name="bites"/> is the shipped Meal
    /// schedule from the activity profile, returned unchanged for everything that authors the
    /// bites style — so the Meal path is bit-identical and only an item that asks for the
    /// single raise gets one.
    /// </summary>
    public ConsumeGesture ToConsumeGesture(in ConsumeGesture bites) =>
        ConsumeStyle == ConsumeGestureStyle.SingleRaise
            ? ConsumeGesture.SingleRaise(
                bites.ChestHoldTicks, ConsumeRaiseTicks, ConsumeHoldTicks)
            : bites;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (string.IsNullOrWhiteSpace(ContentId))
            errors.Add($"{nameof(ContentId)} must be a stable non-empty string");
        if (!float.IsFinite(Radius) || Radius <= 0.0f)
            errors.Add($"{nameof(Radius)} must be finite and positive");
        if (!float.IsFinite(Mass) || Mass <= 0.0f)
            errors.Add($"{nameof(Mass)} must be finite and positive");
        if (!float.IsFinite(LinearDamp) || LinearDamp < 0.0f)
            errors.Add($"{nameof(LinearDamp)} must be finite and non-negative");
        if (!float.IsFinite(AngularDamp) || AngularDamp < 0.0f)
            errors.Add($"{nameof(AngularDamp)} must be finite and non-negative");
        if (!float.IsFinite(Bounce) || Bounce < 0.0f || Bounce > 1.0f)
            errors.Add($"{nameof(Bounce)} must be finite and between zero and one");
        if (!float.IsFinite(RestSpeedThreshold) || RestSpeedThreshold < 0.0f)
            errors.Add($"{nameof(RestSpeedThreshold)} must be finite and non-negative");
        if (RestTicksRequired <= 0)
            errors.Add($"{nameof(RestTicksRequired)} must be positive");
        if (Launch is not null &&
            (!GodotObject.IsInstanceValid(Launch) || !Launch.IsRuntimeValid))
        {
            errors.Add($"{nameof(Launch)} must be a valid pullback launcher profile when set");
        }
        if (SoccerPlay is not null &&
            (!GodotObject.IsInstanceValid(SoccerPlay) || !SoccerPlay.IsRuntimeValid))
        {
            errors.Add($"{nameof(SoccerPlay)} must be a valid soccer play profile when set");
        }
        if (Hazardous && SafeToEvict)
            errors.Add("Hazardous loose objects cannot be marked safe to evict");
        if (Consumable && (!float.IsFinite(ConsumeMoodGain) || ConsumeMoodGain <= 0.0f))
            errors.Add($"{nameof(ConsumeMoodGain)} must be finite and positive for a consumable");
        if (Consumable && ConsumeCooldownTicks < 0)
            errors.Add($"{nameof(ConsumeCooldownTicks)} cannot be negative");
        if (Consumable && (!float.IsFinite(ConsumeHungerFill) || ConsumeHungerFill < 0.0f))
            errors.Add($"{nameof(ConsumeHungerFill)} must be finite and non-negative");
        if (Consumable && (ConsumeRaiseTicks <= 0 || ConsumeHoldTicks <= 0))
            errors.Add($"{nameof(ConsumeRaiseTicks)} and {nameof(ConsumeHoldTicks)} must be positive");
        return errors;
    }
}
