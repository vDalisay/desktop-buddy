using DesktopBuddy.App;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>Physics-laboratory tuning for the sandbox wall geometry.</summary>
[GlobalClass]
public partial class BoundaryProfile : GameResource
{
    [Export(PropertyHint.Range, "1,64,0.5")]
    public float WallThickness { get; set; } = 16.0f;

    [Export(PropertyHint.Range, "0,64,0.5")]
    public float SafePoseFloorClearance { get; set; } = 12.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!float.IsFinite(WallThickness) || WallThickness <= 0.0f ||
            WallThickness * 2.0f >= RoomLayoutPolicy.MinimumRoomHeight)
        {
            errors.Add("WallThickness must be finite, positive, and leave usable room space.");
        }

        if (!float.IsFinite(SafePoseFloorClearance) || SafePoseFloorClearance < 0.0f)
        {
            errors.Add("SafePoseFloorClearance must be finite and non-negative.");
        }

        return errors;
    }
}
