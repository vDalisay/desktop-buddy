# Stitched Doll Look — Owner Direction and Implementation

Status: **implemented on `feature/stitched-doll-look`; interactive acceptance pending**

Owner direction recorded 2026-08-04: restyle the existing Desktop Buddy as an original
stitched fabric doll inspired by the supplied broad visual direction. Keep the body in warm
brown cloth colours and do not include any pre-painted splatters.

## Locked visual target

- Warm brown head, torso, hands, feet, and connectors.
- Neutral woven-cloth detail multiplied by the selected part colour, so recolouring remains
  functional rather than baking one fixed brown texture into character documents.
- Visible dark seams with light cross-stitches on every trusted body-part mesh.
- Dark button eyes with fixed light X-thread.
- Existing expressive brows and mouth remain active.
- No built-in paint pixels, paint splatters, torso patch, or accent decal.
- Existing primitive anatomy, ragdoll physics, collision, hit mapping, paint UVs, reactions,
  economy, and tools remain unchanged.

## Layering contract

The stitched style preserves the Phase B paint architecture:

1. trusted body mesh with neutral fabric texture and selected base colour;
2. transparent player-paint shell at grow `0.05`;
3. transparent stitch shell at grow `0.075`;
4. trusted face and torso-accent plates at the existing `0.1` epsilon;
5. inverted-hull outline.

This keeps player paint editable and removable while the doll construction seams remain part
of the base character identity. The stitch shell shares the trusted mesh and has no physics,
input, persistence, or hit-mapping authority.

## Files

- `assets/buddy/stitched_doll_fabric.svg`
- `assets/buddy/stitched_doll_seams.svg`
- `data/buddy/lab_buddy_look.tres`
- `data/buddy/lab_buddy_visual.tres`
- `src/Buddy/Presentation3D/BuddyLookProfile.cs`
- `src/Buddy/Presentation3D/BuddyLookMaterialLibrary.cs`
- `src/Buddy/Presentation3D/Characters/ButtonEyeRenderer.cs`
- shipped character catalog/default appearance files and their focused tests

## Required verification before merge

1. Build the Godot .NET project with zero errors and warnings.
2. Run the domain character tests and `expression_renderer_coverage`.
3. Run the existing presentation, character-rig, painting, and quick-validation scenarios.
4. In the live game and character editor, inspect all six parts while idle, moving, rotated,
   grabbed, scorched, and painted.
5. Confirm the fabric is readable without moire, seams do not z-fight, face/accent decals stay
   above stitches, and painting still lands on the same UV positions.
6. Owner accepts the exact brown palette, weave strength, seam placement, stitch thickness,
   and button-eye scale.

The supplied image is a visual-direction reference only. No source asset, branding, layout,
paint pattern, or distinctive composition from it is committed.
