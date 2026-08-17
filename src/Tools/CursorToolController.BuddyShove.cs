using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

public partial class CursorToolController
{
    private bool _buddyShoveHooked;

    public override void _Ready()
    {
        if (_buddyShoveHooked || !GodotObject.IsInstanceValid(Pipeline))
            return;

        Pipeline.ImpactAccepted += OnCaptureSwingImpactAccepted;
        TreeExiting += UnhookCaptureSwingImpact;
        _buddyShoveHooked = true;
    }

    private void UnhookCaptureSwingImpact()
    {
        if (!_buddyShoveHooked)
            return;
        if (GodotObject.IsInstanceValid(Pipeline))
            Pipeline.ImpactAccepted -= OnCaptureSwingImpactAccepted;
        _buddyShoveHooked = false;
    }

    private void OnCaptureSwingImpactAccepted(AcceptedImpact impact)
    {
        if (impact.ContentId != ContentIds.ToolBaseballBat || impact.SwingEpoch <= 0 ||
            SwingProfileForContent(impact.ContentId) is not SwingToolProfile swing)
        {
            return;
        }

        float totalImpulse = swing.BuddyShoveForCharge(impact.SwingCharge);
        if (totalImpulse <= 0.0f)
            return;

        var parts = Pipeline.Buddy.Rig.Parts;
        if (parts.Count == 0)
            return;

        PuppetPartBody? struck = null;
        float totalMass = 0.0f;
        for (int index = 0; index < parts.Count; index++)
        {
            PuppetPartBody part = parts[index];
            if (!GodotObject.IsInstanceValid(part) || part.Freeze || !float.IsFinite(part.Mass) || part.Mass <= 0.0f)
                continue;
            totalMass += part.Mass;
            if ((int)part.PartId == (int)impact.Part)
                struck = part;
        }
        if (totalMass <= 0.0f)
            return;

        Vector2 direction = Vector2.Zero;
        if (GodotObject.IsInstanceValid(_body) && GodotObject.IsInstanceValid(struck))
            direction = struck!.GlobalPosition - _body!.GlobalPosition;
        if (direction.LengthSquared() <= 0.0001f && GodotObject.IsInstanceValid(_body))
            direction = _body!.LinearVelocity;
        if (direction.LengthSquared() <= 0.0001f && impact.Normal.LengthSquared() > 0.0001f)
            direction = -impact.Normal;
        if (direction.LengthSquared() <= 0.0001f)
            direction = Vector2.Up;
        direction = direction.Normalized();

        // Every part receives the same delta-v because its share is proportional to its mass.
        // The links therefore translate with the hit instead of one small limb eating the entire
        // home-run impulse and stretching away from the torso.
        for (int index = 0; index < parts.Count; index++)
        {
            PuppetPartBody part = parts[index];
            if (!GodotObject.IsInstanceValid(part) || part.Freeze || part.Mass <= 0.0f)
                continue;
            float share = totalImpulse * (part.Mass / totalMass);
            part.ApplyCentralImpulse(direction * share);
        }
    }
}
