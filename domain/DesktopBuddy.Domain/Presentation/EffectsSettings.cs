using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>
/// The accessibility and content-sensitivity effect settings, snapshotted out of
/// <see cref="LocalSettingsSave"/> and handed to presentation components (FR-017.3,
/// FR-017.6).
///
/// <para><b>Gameplay never reads this.</b> It reaches presenters and nothing else, so
/// flipping every toggle changes what a run looks and sounds like and cannot change one
/// tick of what it simulates. That is asserted, not assumed: the burning scenario runs the
/// same seed twice with all four flipped and compares pain, mood, and tick counts.</para>
///
/// <para>The seam exists from the first slice that ships effects honoring it rather than
/// being deferred to the accessibility pass, because a shipped effect that ignores the
/// setting is the thing FR-017.3 forbids.</para>
/// </summary>
public readonly record struct EffectsSettings(
    /// <summary>Damp or drop large presentation motion. Default off.</summary>
    bool ReducedMotion,

    /// <summary>Whether the camera-kick lane may move rendered content at all. Default on.</summary>
    bool ScreenShake,

    /// <summary>Thin particle-like effects to a fraction of their full count. Default off.</summary>
    bool ReducedParticles,

    /// <summary>Cap flicker and strobing modulation. Default <b>on</b>: the safe look ships.</summary>
    bool PhotosensitivitySafe,

    /// <summary>
    /// Gore Mode: whether piercing hits open bleeding wounds and stain the room. Default
    /// <b>off</b>, on the same principle as <see cref="PhotosensitivitySafe"/> — the mild
    /// look ships and the player opts in.
    ///
    /// <para>This flag lives here precisely because bleeding is presentation. It reaches
    /// presenters and nothing else, so the determinism rule above covers it: the same seed
    /// with Gore Mode on and off produces the same pain, mood, and tick counts. A build
    /// that does not ship the feature at all refuses it a second time at the composition
    /// root, so a hand-edited settings file cannot switch it on.</para>
    /// </summary>
    bool Gore)
{
    /// <summary>The FR-017.6 defaults, used before any save has been loaded.</summary>
    public static EffectsSettings Default => new(
        ReducedMotion: false,
        ScreenShake: true,
        ReducedParticles: false,
        PhotosensitivitySafe: true,
        Gore: false);

    /// <summary>Everything a photosensitive, motion-sensitive player would turn on.</summary>
    public static EffectsSettings MostRestrictive => new(
        ReducedMotion: true,
        ScreenShake: false,
        ReducedParticles: true,
        PhotosensitivitySafe: true,
        Gore: false);

    public static EffectsSettings FromSave(LocalSettingsSave? save) =>
        save is null
            ? Default
            : new EffectsSettings(
                save.ReducedMotion,
                save.ScreenShake,
                save.ReducedParticles,
                save.PhotosensitivitySafe,
                save.GoreEnabled);

    /// <summary>
    /// How many of every N candidate particles are actually drawn. One under reduced
    /// particles keeps the effect legible while visibly thinning it; three is every one.
    /// </summary>
    public int ParticleStride => ReducedParticles ? 3 : 1;

    /// <summary>
    /// The flicker rate a presenter may modulate at, given its authored safe and full
    /// rates. The safe cap is the shipped look and the faster rate is the opt-out.
    /// </summary>
    public float FlickerHz(float safeHz, float fullHz) =>
        PhotosensitivitySafe || ReducedMotion ? safeHz : fullHz;
}
