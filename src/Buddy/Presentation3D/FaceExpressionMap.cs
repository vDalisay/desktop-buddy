using System.Collections.Generic;
using DesktopBuddy.Domain.Presentation;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// The face art styles the compositor can paint. The M3.6 mockup gate (DECISIONS.md
/// 2026-07-20) shipped <see cref="SoftOval"/> as the default; InkDots and BeanBlush were
/// retained as FUTURE SHOP COSMETICS (M5 economy scope) — their ids are reserved here so
/// save data and the shop can reference them later, but only SoftOval has a painter in
/// this slice.
/// </summary>
public enum FaceStyleId
{
    SoftOval = 0,
    // Reserved for the M5 shop: InkDots = 1, BeanBlush = 2 (see scenes/spike_face_mockup.tscn
    // for the accepted reference art of all three).
}

/// <summary>
/// The authoritative face-state translation seam (M3_6_EXPRESSIVE_PRESENTATION_PLAN.md
/// Task 5): every string <c>BuddyReactionComponent.Resolve</c> can produce, mapped to a
/// variant-agnostic <see cref="FaceFeaturePose"/>. The strings and the resolver do NOT
/// change (prime invariant 3) — this map only translates them for the compositor. The
/// mapping itself lives engine-free in <see cref="FaceExpressionCatalog"/> so coverage is
/// dotnet-tested; this alias exists so Godot-side code has one named seam to import and
/// the list is discoverable beside the presentation components.
/// </summary>
public static class FaceExpressionMap
{
    /// <summary>Every semantic face string, in resolver priority order.</summary>
    public static IReadOnlyList<string> Faces => FaceExpressionCatalog.Faces;

    public static bool TryResolve(string face, out FaceFeaturePose pose) =>
        FaceExpressionCatalog.TryResolve(face, out pose);

    /// <summary>Resolves or throws — an unknown face string is a broken contract, never a default.</summary>
    public static FaceFeaturePose Resolve(string face) => FaceExpressionCatalog.Resolve(face);
}
