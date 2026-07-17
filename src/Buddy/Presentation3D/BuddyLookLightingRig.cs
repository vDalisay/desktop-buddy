using System;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// The transparent-safe two-light rig for the shipping 3D presentation. It owns exactly one
/// warm key and one cool camera-axis fill <see cref="DirectionalLight3D"/>, both with shadows
/// disabled, configured from the injected <see cref="BuddyLookProfile"/>. It creates no
/// <see cref="WorldEnvironment"/> (a sky or opaque background would paint over the desktop and
/// kill the transparent shell — M3.5 Task 1 outcome). The scene root composes and initializes
/// it from the same look Resource the presenter uses. Startup fails loudly on a missing or
/// invalid profile rather than silently reverting to flat output (L3).
/// </summary>
[GlobalClass]
public partial class BuddyLookLightingRig : Node3D
{
    [Export] public BuddyLookProfile Look { get; set; } = null!;

    public DirectionalLight3D KeyLight { get; private set; } = null!;
    public DirectionalLight3D FillLight { get; private set; } = null!;
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Builds the two directional lights once from the look profile. Idempotent so a scene
    /// root that re-runs composition cannot duplicate the rig.
    /// </summary>
    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Look))
        {
            throw new InvalidOperationException(
                "BuddyLookLightingRig requires an injected look profile.");
        }

        Godot.Collections.Array<string> errors = Look.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy look profile: {string.Join("; ", errors)}");
        }

        // The presenter-driven 3D nodes and the world camera disable engine interpolation
        // (global constraint 6); the light rig matches so a queued layout snap does not ease.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        KeyLight = CreateLight("KeyLight", Look.KeyColor, Look.KeyEnergy, Look.KeyEulerDegrees);
        FillLight = CreateLight("FillLight", Look.FillColor, Look.FillEnergy, Look.FillEulerDegrees);
        AddChild(KeyLight);
        AddChild(FillLight);

        IsInitialized = true;
    }

    private static DirectionalLight3D CreateLight(
        string name,
        Color color,
        float energy,
        Vector3 eulerDegrees) => new()
    {
        Name = name,
        LightColor = color,
        LightEnergy = energy,
        ShadowEnabled = false,
        RotationDegrees = eulerDegrees,
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
    };
}
