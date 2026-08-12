using System;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Transparent-safe two-light rig for the shipping 3D presentation. Light construction is shared
/// with Asset Forge so preview and runtime use the same accepted key/fill setup.
/// </summary>
[GlobalClass]
public partial class BuddyLookLightingRig : Node3D
{
    public DirectionalLight3D KeyLight { get; private set; } = null!;
    public DirectionalLight3D FillLight { get; private set; } = null!;
    public bool IsInitialized { get; private set; }

    public void Initialize(BuddyLookProfile look)
    {
        if (IsInitialized) return;
        if (!GodotObject.IsInstanceValid(look))
            throw new InvalidOperationException("BuddyLookLightingRig requires an injected look profile.");

        Godot.Collections.Array<string> errors = look.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid buddy look profile: {string.Join("; ", errors)}");

        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        KeyLight = CreateLight("KeyLight", look.KeyColor, look.KeyEnergy, look.KeyEulerDegrees);
        FillLight = CreateLight("FillLight", look.FillColor, look.FillEnergy, look.FillEulerDegrees);
        AddChild(KeyLight);
        AddChild(FillLight);
        IsInitialized = true;
    }

    private static DirectionalLight3D CreateLight(string name, Color color, float energy, Vector3 eulerDegrees)
    {
        DirectionalLight3D light = BuddySharedMaterialFactory.CreateDirectionalLight(name, color, energy, eulerDegrees);
        light.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit;
        return light;
    }
}
