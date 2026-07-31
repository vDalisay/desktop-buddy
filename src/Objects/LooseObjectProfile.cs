using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
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
    /// Optional pullback tuning used when the player launches <i>this</i> item.
    /// <c>null</c> — the case for every launchable authored before the Soccer Ball — means the
    /// launcher's own shared preset, so nothing that did not author one changes. Authored
    /// rather than shared because a playground ball should leave the hand slower and loopier
    /// than a baseball, and that is a per-item feel number.
    /// </summary>
    [Export] public Tools.PullbackLauncherProfile? Launch { get; set; }

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
        !(Hazardous && SafeToEvict) &&
        (!Consumable ||
         (float.IsFinite(ConsumeMoodGain) && ConsumeMoodGain > 0.0f &&
          ConsumeCooldownTicks >= 0 &&
          float.IsFinite(ConsumeHungerFill) && ConsumeHungerFill >= 0.0f));

    /// <summary>
    /// The approved consume tuning for this item. The cooldown/one-success rule itself lives
    /// in <see cref="CareConsumableModel"/>; this Resource only says how much and how long.
    /// </summary>
    public CareConsumableTuning ToConsumableTuning() =>
        new(ConsumeMoodGain, ConsumeCooldownTicks);

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
        if (Hazardous && SafeToEvict)
            errors.Add("Hazardous loose objects cannot be marked safe to evict");
        if (Consumable && (!float.IsFinite(ConsumeMoodGain) || ConsumeMoodGain <= 0.0f))
            errors.Add($"{nameof(ConsumeMoodGain)} must be finite and positive for a consumable");
        if (Consumable && ConsumeCooldownTicks < 0)
            errors.Add($"{nameof(ConsumeCooldownTicks)} cannot be negative");
        if (Consumable && (!float.IsFinite(ConsumeHungerFill) || ConsumeHungerFill < 0.0f))
            errors.Add($"{nameof(ConsumeHungerFill)} must be finite and non-negative");
        return errors;
    }
}
