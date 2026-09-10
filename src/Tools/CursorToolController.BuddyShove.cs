using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

public partial class CursorToolController
{
    private readonly List<(InteractionDamageComponent Damage, Action<AcceptedImpact> Handler)> _shoveHooks = [];
    private bool _buddyShoveHooked;

    public override void _Ready()
    {
        if (_buddyShoveHooked)
            return;

        HookCaptureSwingImpacts();
        if (GodotObject.IsInstanceValid(Sandbox))
            Sandbox!.SceneRosterChanged += HookCaptureSwingImpacts;
        TreeExiting += UnhookCaptureSwingImpact;
        _buddyShoveHooked = true;
    }

    /// <summary>
    /// Binds the shove to a multi-Buddy room. Child nodes are ready before the sandbox is, so the
    /// room hands itself over here rather than through the export alone.
    /// </summary>
    public void BindShoveRoom(SandboxRoot sandbox)
    {
        ArgumentNullException.ThrowIfNull(sandbox);
        if (ReferenceEquals(Sandbox, sandbox))
            return;
        if (GodotObject.IsInstanceValid(Sandbox))
            Sandbox!.SceneRosterChanged -= HookCaptureSwingImpacts;
        Sandbox = sandbox;
        sandbox.SceneRosterChanged += HookCaptureSwingImpacts;
        HookCaptureSwingImpacts();
    }

    /// <summary>
    /// A home-run swing launches the Buddy it actually connected with, so the shove listens to every
    /// live Buddy's pipeline and re-attaches when the room's cast changes.
    /// </summary>
    private void HookCaptureSwingImpacts()
    {
        DetachShoveHooks();
        foreach ((BuddyRoot buddy, InteractionDamageComponent damage) in ShoveTargets())
        {
            BuddyRoot struckBuddy = buddy;
            void Handler(AcceptedImpact impact) => OnCaptureSwingImpactAccepted(impact, struckBuddy);
            damage.ImpactAccepted += Handler;
            _shoveHooks.Add((damage, Handler));
        }
    }

    private IEnumerable<(BuddyRoot Buddy, InteractionDamageComponent Damage)> ShoveTargets()
    {
        if (GodotObject.IsInstanceValid(Sandbox))
            return Sandbox!.LiveCast();
        return GodotObject.IsInstanceValid(Pipeline) && GodotObject.IsInstanceValid(Pipeline.Buddy)
            ? [(Pipeline.Buddy, Pipeline)]
            : [];
    }

    private void DetachShoveHooks()
    {
        foreach ((InteractionDamageComponent damage, Action<AcceptedImpact> handler) in _shoveHooks)
        {
            if (GodotObject.IsInstanceValid(damage))
                damage.ImpactAccepted -= handler;
        }
        _shoveHooks.Clear();
    }

    private void UnhookCaptureSwingImpact()
    {
        if (!_buddyShoveHooked)
            return;
        DetachShoveHooks();
        if (GodotObject.IsInstanceValid(Sandbox))
            Sandbox!.SceneRosterChanged -= HookCaptureSwingImpacts;
        _buddyShoveHooked = false;
    }

    private void OnCaptureSwingImpactAccepted(AcceptedImpact impact, BuddyRoot struckBuddy)
    {
        if (impact.ContentId != ContentIds.ToolBaseballBat || impact.SwingEpoch <= 0 ||
            SwingProfileForContent(impact.ContentId) is not SwingToolProfile swing)
        {
            return;
        }

        float totalImpulse = swing.BuddyShoveForCharge(impact.SwingCharge);
        if (totalImpulse <= 0.0f)
            return;

        if (!GodotObject.IsInstanceValid(struckBuddy))
            return;
        var parts = struckBuddy.Rig.Parts;
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
