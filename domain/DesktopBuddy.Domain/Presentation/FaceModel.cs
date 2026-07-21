using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>
/// Eye feature pose, variant-agnostic: painters interpret these semantically (an "open"
/// eye is an ink dot in one style and a highlighted oval in another), so the pose list is
/// the contract between the expression map and every face art style.
/// </summary>
public enum FaceEyePose
{
    /// <summary>Regular open eyes — blinkable, carries pupils.</summary>
    Open = 0,
    /// <summary>Narrowed open eyes (anger) — blinkable, carries pupils.</summary>
    Narrow = 1,
    /// <summary>Wide startle eyes — pupils but NO blink (a blink would erase the startle read).</summary>
    Wide = 2,
    /// <summary>Closed happy arcs (delight) — no blink, no pupils.</summary>
    HappyArc = 3,
    /// <summary>Squeezed-shut pain scrunch — no blink, no pupils.</summary>
    Scrunch = 4,
    /// <summary>Unconscious crosses — no blink, no pupils.</summary>
    Cross = 5,
}

public enum FaceBrowPose
{
    None = 0,
    Neutral = 1,
    Raised = 2,
    AngledIn = 3,
    Worried = 4,
}

public enum FaceMouthPose
{
    Flat = 0,
    Smile = 1,
    OpenSmile = 2,
    CatSmile = 3,
    Frown = 4,
    Squiggle = 5,
    SmallO = 6,
    Slant = 7,
    /// <summary>Eat overlay frame: mouth open around the bite.</summary>
    ChewOpen = 8,
    /// <summary>Eat overlay frame: mouth closed on the bite.</summary>
    ChewClosed = 9,
}

/// <summary>One semantic face state expressed as composable features.</summary>
public readonly record struct FaceFeaturePose(
    FaceEyePose Eyes,
    FaceBrowPose Brows,
    FaceMouthPose Mouth)
{
    /// <summary>Blink only makes sense over eyes that are drawn open and neutral-lidded.</summary>
    public bool EyesBlinkable => Eyes is FaceEyePose.Open or FaceEyePose.Narrow;

    /// <summary>Pupils render on any open-eye pose; closed/special eyes have none.</summary>
    public bool HasPupils => Eyes is FaceEyePose.Open or FaceEyePose.Narrow or FaceEyePose.Wide;
}

/// <summary>
/// The authoritative semantic-face-string to feature-pose map
/// (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md Task 5). The string list mirrors
/// <c>BuddyReactionComponent.Resolve</c> exactly — the strings and the resolver do not
/// change; this catalog only translates them. The Godot <c>FaceExpressionMap</c> exports
/// this list beside the resolver and asserts coverage at initialization.
/// </summary>
public static class FaceExpressionCatalog
{
    private static readonly Dictionary<string, FaceFeaturePose> Poses = new()
    {
        // Neutral band.
        [":|"] = new FaceFeaturePose(FaceEyePose.Open, FaceBrowPose.Neutral, FaceMouthPose.Flat),
        // Unconscious.
        ["x_x"] = new FaceFeaturePose(FaceEyePose.Cross, FaceBrowPose.None, FaceMouthPose.SmallO),
        // Acute pain.
        [">_<"] = new FaceFeaturePose(FaceEyePose.Scrunch, FaceBrowPose.AngledIn, FaceMouthPose.Squiggle),
        // Angry tickle disposition / defending.
        [">:("] = new FaceFeaturePose(FaceEyePose.Narrow, FaceBrowPose.AngledIn, FaceMouthPose.Frown),
        // Acute fear / feared tool.
        ["o_o"] = new FaceFeaturePose(FaceEyePose.Wide, FaceBrowPose.Raised, FaceMouthPose.SmallO),
        // Pet completion smile / content band.
        [":)"] = new FaceFeaturePose(FaceEyePose.Open, FaceBrowPose.Neutral, FaceMouthPose.Smile),
        // Pet rubbing.
        [":3"] = new FaceFeaturePose(FaceEyePose.HappyArc, FaceBrowPose.None, FaceMouthPose.CatSmile),
        // Tickle contact / delight / delighted band.
        ["^_^"] = new FaceFeaturePose(FaceEyePose.HappyArc, FaceBrowPose.None, FaceMouthPose.OpenSmile),
        // Fearful band.
        [":("] = new FaceFeaturePose(FaceEyePose.Open, FaceBrowPose.Worried, FaceMouthPose.Frown),
        // Wary band.
        [":/"] = new FaceFeaturePose(FaceEyePose.Open, FaceBrowPose.Worried, FaceMouthPose.Slant),
    };

    /// <summary>Every face string the resolver can produce, in resolver priority order.</summary>
    public static readonly IReadOnlyList<string> Faces = new[]
    {
        "x_x", ">_<", ">:(", "o_o", ":)", ":3", "^_^", ":(", ":/", ":|",
    };

    public static bool TryResolve(string face, out FaceFeaturePose pose) =>
        Poses.TryGetValue(face, out pose);

    /// <summary>Resolves or throws — an unknown face string is a contract break, never a default.</summary>
    public static FaceFeaturePose Resolve(string face) =>
        TryResolve(face, out FaceFeaturePose pose)
            ? pose
            : throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown semantic face string.");
}

/// <summary>Blink tuning subset consumed by the pure model.</summary>
public readonly record struct BlinkParameters(
    int IntervalMinimumTicks,
    int IntervalMaximumTicks,
    int ClosedTicks);

/// <summary>
/// Seeded blink timer: a random open interval, then closed for a fixed short hold, then a
/// fresh interval. Counts in ROUTED ticks (the simulation's own clock) so a laboratory
/// pause freezes it. While suppressed (closed/special-eye face states) the timer disarms
/// completely — it draws nothing from the stream — and re-arms with a fresh interval when
/// the face becomes blinkable again.
/// </summary>
public sealed class BlinkModel
{
    private readonly IRandomSource _random;
    private readonly BlinkParameters _parameters;

    private bool _armed;
    private int _ticksRemaining;

    public BlinkModel(IRandomSource random, in BlinkParameters parameters)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
        if (parameters.IntervalMinimumTicks < 1 ||
            parameters.IntervalMaximumTicks <= parameters.IntervalMinimumTicks ||
            parameters.ClosedTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(parameters), parameters, "Invalid blink parameters.");
        }

        _parameters = parameters;
    }

    /// <summary>True while the lids are down on a blink (never true while suppressed).</summary>
    public bool EyesClosed { get; private set; }

    public void Update(bool suppressed, int ticksElapsed)
    {
        if (ticksElapsed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksElapsed), ticksElapsed, "Ticks cannot be negative.");
        }

        if (suppressed)
        {
            _armed = false;
            EyesClosed = false;
            return;
        }

        if (!_armed)
        {
            _armed = true;
            EyesClosed = false;
            _ticksRemaining = NextInterval();
        }

        _ticksRemaining -= ticksElapsed;
        if (_ticksRemaining > 0)
        {
            return;
        }

        if (EyesClosed)
        {
            EyesClosed = false;
            _ticksRemaining = NextInterval();
        }
        else
        {
            EyesClosed = true;
            _ticksRemaining = _parameters.ClosedTicks;
        }
    }

    private int NextInterval() => _random.NextInt(
        _parameters.IntervalMinimumTicks, _parameters.IntervalMaximumTicks + 1);
}

/// <summary>Pure chew-loop frame math for the eat overlay: two frames per cycle.</summary>
public static class ChewCycle
{
    public static int FrameAt(long routedTicks, int chewCycleTicks)
    {
        if (chewCycleTicks < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chewCycleTicks), chewCycleTicks, "A chew cycle needs at least two ticks.");
        }

        if (routedTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routedTicks), routedTicks, "Ticks cannot be negative.");
        }

        return (routedTicks % chewCycleTicks) * 2 / chewCycleTicks == 0 ? 0 : 1;
    }
}

/// <summary>
/// Everything the compositor needs to draw one face, as a value: the re-render key. The
/// compositor repaints exactly when this record changes, which is the plan's
/// "re-render on change only" rule made testable. Compose zeroes out components that are
/// not visible in the final image (pupils under closed lids, chew under a reaction face)
/// so an invisible change can never trigger a repaint.
/// </summary>
public readonly record struct FaceRenderState(
    FaceEyePose Eyes,
    FaceBrowPose Brows,
    FaceMouthPose Mouth,
    bool Blinking,
    float PupilX,
    float PupilY);

public static class FaceComposer
{
    /// <summary>
    /// Combines the semantic pose with the overlays. Chew replaces the mouth only while
    /// the eat activity is live AND the face is not suppression-priority (a punched buddy
    /// shows pain, not chewing); blink only closes blinkable eyes; pupils survive only
    /// where they are actually drawn.
    /// </summary>
    public static FaceRenderState Compose(
        in FaceFeaturePose pose,
        bool blinkClosed,
        bool chewActive,
        int chewFrame,
        bool faceSuppressed,
        float pupilX,
        float pupilY)
    {
        if (chewFrame is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(chewFrame), chewFrame, "Chew frame must be 0 or 1.");
        }

        bool blinking = blinkClosed && pose.EyesBlinkable;
        FaceMouthPose mouth = chewActive && !faceSuppressed
            ? (chewFrame == 0 ? FaceMouthPose.ChewOpen : FaceMouthPose.ChewClosed)
            : pose.Mouth;
        bool pupilsVisible = pose.HasPupils && !blinking;
        return new FaceRenderState(
            pose.Eyes,
            pose.Brows,
            mouth,
            blinking,
            pupilsVisible ? ClampPupil(pupilX) : 0.0f,
            pupilsVisible ? ClampPupil(pupilY) : 0.0f);
    }

    private static float ClampPupil(float value) =>
        !float.IsFinite(value) ? 0.0f : Math.Clamp(value, -1.0f, 1.0f);
}
