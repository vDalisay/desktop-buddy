namespace DesktopBuddy.App;

/// <summary>
/// C# mirror of the named 2D physics layers declared in <c>project.godot</c>
/// (ARCHITECTURE.md Section 20). Layer values are single-bit masks where
/// layer N occupies bit (N-1), matching Godot's 1-based inspector numbering.
///
/// The <c>Mask*</c> members encode the contractual collision table. The
/// buddy-never-collides-with-buddy row is a hard requirement proven by the
/// collision-layer tests; the remaining rows are laboratory-adjustable, so the
/// masks live in one place rather than being scattered across rig setup code.
/// </summary>
public static class CollisionLayers
{
    // Single-bit layer identities (project.godot [layer_names]/2d_physics).
    public const uint RoomBounds = 1u << 0; // layer 1
    public const uint BuddyParts = 1u << 1; // layer 2
    public const uint LooseObjects = 1u << 2; // layer 3
    public const uint Projectiles = 1u << 3; // layer 4
    public const uint PhysicalTools = 1u << 4; // layer 5
    public const uint InteractionSense = 1u << 5; // layer 6

    // Contractual collision masks (what each body type collides with).
    public const uint MaskRoomBounds = BuddyParts | LooseObjects | Projectiles | PhysicalTools;

    // Buddy parts collide with everything physical EXCEPT other buddy parts.
    public const uint MaskBuddyParts = RoomBounds | LooseObjects | Projectiles | PhysicalTools;

    public const uint MaskLooseObjects = RoomBounds | BuddyParts | LooseObjects | Projectiles | PhysicalTools;

    // Projectiles: no projectile-projectile and no projectile-tool.
    public const uint MaskProjectiles = RoomBounds | BuddyParts | LooseObjects;

    public const uint MaskPhysicalTools = RoomBounds | BuddyParts | LooseObjects;

    // Detection-only sensor areas scan loose objects.
    public const uint MaskInteractionSense = LooseObjects;
}
