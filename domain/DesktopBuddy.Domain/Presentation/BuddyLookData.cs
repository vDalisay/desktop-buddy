using System.Collections.Generic;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>A plain RGBA colour sample for pure look validation (no Godot dependency).</summary>
public readonly record struct LookColor(float R, float G, float B, float A)
{
    public bool IsFinite() =>
        float.IsFinite(R) && float.IsFinite(G) && float.IsFinite(B) && float.IsFinite(A);
}

/// <summary>Pitch/yaw/roll degrees for a directional light, validated for finiteness only.</summary>
public readonly record struct LookEuler(float Pitch, float Yaw, float Roll)
{
    public bool IsFinite() =>
        float.IsFinite(Pitch) && float.IsFinite(Yaw) && float.IsFinite(Roll);
}

/// <summary>
/// Pure-logic image of the accepted Variant C production look (M3.5 materials/look plan).
/// The Godot <c>BuddyLookProfile</c> resource copies its exported fields into this struct and
/// delegates <see cref="Validate"/> here, so the numeric/colour/shadow contract is covered by
/// fast <c>dotnet test</c> unit tests without a Godot runtime. Values that are provably
/// meaningless (non-finite energies, out-of-range specular/roughness, a negative outline grow,
/// or shadows enabled on the accepted profile) produce actionable named errors.
/// </summary>
public readonly record struct BuddyLookData(
    int DiffuseMode,
    int SpecularMode,
    float Specular,
    float Roughness,
    LookColor KeyColor,
    float KeyEnergy,
    LookEuler KeyEulerDegrees,
    LookColor FillColor,
    float FillEnergy,
    LookEuler FillEulerDegrees,
    bool ShadowsEnabled,
    LookColor OutlineColor,
    float OutlineGrowAmount)
{
    // Documented mirrors of Godot's BaseMaterial3D enum numbering (StandardMaterial3D):
    // DiffuseMode: Burley=0, Lambert=1, LambertWrap=2, Toon=3.
    // SpecularMode: SchlickGgx=0, Toon=1, Disabled=2.
    public const int MaxDiffuseMode = 3;
    public const int MaxSpecularMode = 2;

    // A shadowless overlay-safe rig never needs bright directional energy; this bound only
    // rejects garbage (NaN cast to a huge value, negative energy), not any plausible tuning.
    public const float MaximumLightEnergy = 64.0f;

    // Inverted-hull grow is in world units; the accepted weight is 1.5. This upper bound only
    // rejects nonsensical shells that would swallow the whole silhouette.
    public const float MaximumOutlineGrowAmount = 64.0f;

    /// <summary>
    /// Returns human-readable validation errors; empty means the look data is acceptable for
    /// the shipping production style. The accepted profile must pass with zero errors.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (DiffuseMode < 0 || DiffuseMode > MaxDiffuseMode)
        {
            errors.Add($"diffuse mode must be within 0-{MaxDiffuseMode}; found {DiffuseMode}");
        }

        if (SpecularMode < 0 || SpecularMode > MaxSpecularMode)
        {
            errors.Add($"specular mode must be within 0-{MaxSpecularMode}; found {SpecularMode}");
        }

        if (!IsFiniteInRange(Specular, 0.0f, 1.0f))
        {
            errors.Add("specular must be finite within 0-1");
        }

        if (!IsFiniteInRange(Roughness, 0.0f, 1.0f))
        {
            errors.Add("roughness must be finite within 0-1");
        }

        if (!IsFiniteInRange(KeyEnergy, 0.0f, MaximumLightEnergy))
        {
            errors.Add($"key light energy must be finite within 0-{MaximumLightEnergy:0}");
        }

        if (!IsFiniteInRange(FillEnergy, 0.0f, MaximumLightEnergy))
        {
            errors.Add($"fill light energy must be finite within 0-{MaximumLightEnergy:0}");
        }

        if (!KeyColor.IsFinite())
        {
            errors.Add("key light colour must be finite");
        }

        if (!FillColor.IsFinite())
        {
            errors.Add("fill light colour must be finite");
        }

        if (!OutlineColor.IsFinite())
        {
            errors.Add("outline colour must be finite");
        }

        if (!KeyEulerDegrees.IsFinite())
        {
            errors.Add("key light euler degrees must be finite");
        }

        if (!FillEulerDegrees.IsFinite())
        {
            errors.Add("fill light euler degrees must be finite");
        }

        if (!float.IsFinite(OutlineGrowAmount) ||
            OutlineGrowAmount <= 0.0f ||
            OutlineGrowAmount > MaximumOutlineGrowAmount)
        {
            errors.Add($"outline grow amount must be finite within (0-{MaximumOutlineGrowAmount:0}]");
        }

        if (ShadowsEnabled)
        {
            errors.Add("accepted transparent-safe profile requires shadows disabled");
        }

        return errors;
    }

    private static bool IsFiniteInRange(float value, float minimum, float maximum) =>
        float.IsFinite(value) && value >= minimum && value <= maximum;
}
