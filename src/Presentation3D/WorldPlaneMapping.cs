using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The single 2D↔3D mapping authority for the frontal 3D presentation (M3.5).
///
/// The 2D simulation is the sole authority; 3D is rendering only. A physics position
/// <c>(x, y)</c> in world pixels maps to <c>(x, −y, 0)</c>, and a 2D rotation maps to
/// <c>−rot2d</c> about Z. The Y flip is required because Godot 2D is Y-down while 3D is
/// Y-up; flipping Y inverts handedness, which is why the Z rotation also changes sign.
///
/// Round-trip contract against the <c>Camera2D</c> pixel mapping: the orthographic
/// <c>WorldCamera3D</c> (see <see cref="Sandbox.BoundaryController"/>) is positioned at
/// <c>(W/2, −H/2, +CameraDistance)</c> looking −Z with <c>Size = RoomHeight</c> and
/// <c>KeepAspect = Height</c>. Under that camera, <see cref="To3D"/> of a world point
/// unprojects to the exact screen pixel the <c>Camera2D</c> renders it at (verified to
/// &lt; 0.5 px by the Task 7 <c>camera_alignment</c> check). CameraDistance and near/far
/// are provably invisible to the orthographic result.
///
/// <see cref="To3DRotationZ"/> applies to <b>every</b> angle crossing the 2D→3D boundary,
/// including <c>PuppetPartBody.FaceDrawRotation</c>'s sideways-emoticon quarter turn:
/// copying a 2D angle verbatim renders a sideways face (the idle <c>":|"</c> is one)
/// flipped by 180°. Route all boundary-crossing angles through this method.
/// </summary>
public static class WorldPlaneMapping
{
    /// <summary>Maps a 2D world position (pixels, Y-down) to its 3D plane point (Y-up).</summary>
    public static Vector3 To3D(Vector2 p) => new(p.X, -p.Y, 0f);

    /// <summary>
    /// Maps a 3D plane point back to its 2D world position (the exact inverse of
    /// <see cref="To3D"/>, discarding the camera-axis depth lane). Presentation-side
    /// consumers that reason about a 3D socket in simulation units — the look-at item
    /// target, for one — must route through here rather than copying components.
    /// </summary>
    public static Vector2 To2D(Vector3 p) => new(p.X, -p.Y);

    /// <summary>Maps a 2D rotation (radians about the 2D Z) to the 3D Z rotation.</summary>
    public static float To3DRotationZ(float rot2d) => -rot2d;
}
