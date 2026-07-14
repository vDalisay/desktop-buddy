using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>Typed ownership and startup configuration for the six bodies.</summary>
[GlobalClass]
public partial class PuppetRig : Node
{
    private readonly PuppetPartBody[] _parts = new PuppetPartBody[PuppetRigProfile.RequiredPartCount];

    [Export] public PuppetRigProfile Profile { get; set; } = null!;
    [Export] public PuppetPartBody Head { get; set; } = null!;
    [Export] public PuppetPartBody Torso { get; set; } = null!;
    [Export] public PuppetPartBody LeftHand { get; set; } = null!;
    [Export] public PuppetPartBody RightHand { get; set; } = null!;
    [Export] public PuppetPartBody LeftFoot { get; set; } = null!;
    [Export] public PuppetPartBody RightFoot { get; set; } = null!;

    public bool IsInitialized { get; private set; }
    public IReadOnlyList<PuppetPartBody> Parts => _parts;

    public void Initialize(Vector2 globalOrigin, IReadOnlyList<Color> fillColors)
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException("PuppetRig requires an injected profile.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Invalid puppet rig profile: {string.Join("; ", errors)}");
        }

        if (fillColors.Count != PuppetRigProfile.RequiredPartCount)
        {
            throw new InvalidOperationException(
                $"PuppetRig requires exactly {PuppetRigProfile.RequiredPartCount} plain fill colors.");
        }

        Assign(BuddyPartId.Head, Head);
        Assign(BuddyPartId.Torso, Torso);
        Assign(BuddyPartId.LeftHand, LeftHand);
        Assign(BuddyPartId.RightHand, RightHand);
        Assign(BuddyPartId.LeftFoot, LeftFoot);
        Assign(BuddyPartId.RightFoot, RightFoot);

        for (int index = 0; index < _parts.Length; index++)
        {
            PuppetPartBody body = _parts[index];
            PuppetPartDefinition definition = Profile.FindPart((BuddyPartId)index)
                ?? throw new InvalidOperationException($"Missing definition for {(BuddyPartId)index}.");
            body.Configure(definition, fillColors[index], globalOrigin);
        }

        IsInitialized = true;
    }

    public PuppetPartBody GetPart(BuddyPartId id)
    {
        int index = (int)id;
        if (index < 0 || index >= _parts.Length || _parts[index] is null)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown puppet part.");
        }

        return _parts[index];
    }

    public bool AllBodiesFinite()
    {
        for (int index = 0; index < _parts.Length; index++)
        {
            if (!_parts[index].HasFiniteState())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Centralized fail-safe pose reset. This is never called by ordinary drive;
    /// <see cref="RecoveryComponent"/> is the sole runtime owner of this seam.
    /// </summary>
    public void ResetToSafePose(Vector2 globalOrigin)
    {
        for (int index = 0; index < _parts.Length; index++)
        {
            PuppetPartBody body = _parts[index];
            PuppetPartDefinition definition = Profile.FindPart((BuddyPartId)index)
                ?? throw new InvalidOperationException($"Missing definition for {(BuddyPartId)index}.");
            body.Freeze = false;
            body.GlobalPosition = globalOrigin + definition.RestPosition;
            body.GlobalRotation = 0.0f;
            body.LinearVelocity = Vector2.Zero;
            body.AngularVelocity = 0.0f;
            body.Sleeping = false;
            body.ResetPhysicsInterpolation();
        }
    }

    private void Assign(BuddyPartId expected, PuppetPartBody body)
    {
        if (!GodotObject.IsInstanceValid(body))
        {
            throw new InvalidOperationException($"PuppetRig requires an injected {expected} body.");
        }

        if (body.PartId != expected)
        {
            throw new InvalidOperationException($"Expected {expected} body; injected {body.PartId}.");
        }

        _parts[(int)expected] = body;
    }
}
