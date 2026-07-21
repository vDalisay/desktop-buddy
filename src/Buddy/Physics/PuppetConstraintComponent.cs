using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Physics;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// Applies each configured structural link once per routed fixed tick using
/// equal-and-opposite forces. It never assigns runtime transforms or velocity.
/// </summary>
[GlobalClass]
public partial class PuppetConstraintComponent : Node
{
    private RuntimeLink[] _links = Array.Empty<RuntimeLink>();
    private LinkTelemetry[] _telemetry = Array.Empty<LinkTelemetry>();

    [Export] public PuppetRig Rig { get; set; } = null!;

    public bool IsInitialized { get; private set; }
    public IReadOnlyList<LinkTelemetry> Telemetry => _telemetry;

    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized)
        {
            throw new InvalidOperationException("PuppetConstraintComponent requires an initialized PuppetRig.");
        }

        int count = Rig.Profile.Links.Count;
        _links = new RuntimeLink[count];
        _telemetry = new LinkTelemetry[count];
        for (int index = 0; index < count; index++)
        {
            PuppetLinkDefinition definition = Rig.Profile.Links[index];
            _links[index] = new RuntimeLink(
                Rig.GetPart(definition.PartA),
                Rig.GetPart(definition.PartB),
                definition);
        }

        IsInitialized = true;
    }

    public void PhysicsTick(bool airborneGrab = false)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("PuppetConstraintComponent was ticked before initialization.");
        }

        for (int index = 0; index < _links.Length; index++)
        {
            ref readonly RuntimeLink link = ref _links[index];
            PuppetLinkDefinition definition = link.Definition;

            Vector2 anchorA = link.A.ToGlobal(definition.LocalAnchorA);
            Vector2 anchorB = link.B.ToGlobal(definition.LocalAnchorB);
            Vector2 actualOffset = anchorB - anchorA;
            Vector2 relativeVelocity = VelocityAt(link.B, anchorB) - VelocityAt(link.A, anchorA);
            Vector2 restOffset = definition.RestOffset.Rotated(link.A.GlobalRotation);
            float stiffnessMultiplier = airborneGrab
                ? Rig.Profile.AirborneGrabStiffnessMultiplier
                : 1.0f;
            float dampingMultiplier = airborneGrab
                ? Rig.Profile.AirborneGrabDampingMultiplier
                : 1.0f;

            var input = new PassiveSpringInput(
                ToNumerics(actualOffset),
                ToNumerics(relativeVelocity),
                ToNumerics(restOffset),
                definition.Stiffness * stiffnessMultiplier,
                definition.Damping * dampingMultiplier,
                definition.MaximumDistance,
                definition.LimitStiffness,
                definition.MaximumForce);

            PassiveSpringResult result = PassiveSpring.Evaluate(input);
            Vector2 forceOnA = ToGodot(result.ForceOnA);
            link.A.ApplyForce(forceOnA, anchorA - link.A.GlobalPosition);
            link.B.ApplyForce(-forceOnA, anchorB - link.B.GlobalPosition);

            _telemetry[index] = new LinkTelemetry(
                definition.LinkId,
                result.Separation,
                result.Strain,
                forceOnA,
                result.LimitActive,
                result.ForceClamped);
        }
    }

    private static Vector2 VelocityAt(PuppetPartBody body, Vector2 worldPoint)
    {
        Vector2 offset = worldPoint - body.GlobalPosition;
        Vector2 angularVelocity = new(-offset.Y, offset.X);
        return body.LinearVelocity + (angularVelocity * body.AngularVelocity);
    }

    private static NumericsVector2 ToNumerics(Vector2 value) => new(value.X, value.Y);
    private static Vector2 ToGodot(NumericsVector2 value) => new(value.X, value.Y);

    private readonly record struct RuntimeLink(
        PuppetPartBody A,
        PuppetPartBody B,
        PuppetLinkDefinition Definition);
}
