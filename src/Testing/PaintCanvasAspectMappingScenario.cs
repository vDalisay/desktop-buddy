using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Regression coverage for a wide editor preview. The SubViewportContainer resizes its
/// SubViewport after layout, so pointer mapping must use the canvas's live aspect rather than
/// the preview's initial 420x360 authoring size.
/// </summary>
public sealed class PaintCanvasAspectMappingScenario : IScenario
{
    public string Id => "paint_canvas_aspect_mapping";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var canvas = new CharacterEditor.PaintCanvasControl
        {
            // Matches the wide preview geometry from the reported regression.
            Size = new Vector2(1034, 360),
        };

        try
        {
            float pixelsPerWorldUnit = canvas.Size.Y /
                (float)CharacterEditor.PaintCanvasControl.BaseCameraSize;
            Vector2 WorldToCanvas(PaintPoint world) => new(
                (canvas.Size.X * 0.5f) + ((float)world.X * pixelsPerWorldUnit),
                (canvas.Size.Y * 0.5f) + ((float)world.Y * pixelsPerWorldUnit));

            PaintPart? torso = canvas.PartAt(WorldToCanvas(new PaintPoint(0, 0)));
            PaintPart? leftHand = canvas.PartAt(WorldToCanvas(new PaintPoint(-38, -5)));
            PaintPart? rightHand = canvas.PartAt(WorldToCanvas(new PaintPoint(38, -5)));
            PaintPart? outside = canvas.PartAt(WorldToCanvas(new PaintPoint(60, 0)));

            bool aligned = torso == PaintPart.Torso &&
                leftHand == PaintPart.LeftHand &&
                rightHand == PaintPart.RightHand &&
                outside is null;
            checks.Add(new StartupCheck(
                "phase_b_wide_canvas_uses_live_camera_aspect",
                aligned,
                $"torso={torso} left={leftHand} right={rightHand} outside={outside}"));
        }
        finally
        {
            canvas.Free();
        }

        return Task.FromResult(PaintingScenarioSupport.Result(checks, seed));
    }
}
