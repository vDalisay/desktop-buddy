using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Physics-free visual transform source for editor previews. It snapshots the trusted
/// six-part rest anatomy once and thereafter owns only immutable value arrays: no
/// <see cref="BuddyRoot"/>, <see cref="RigidBody2D"/>, solver, reaction component, or clock.
/// </summary>
public sealed class StaticBuddyVisualTransformSource : IBuddyVisualTransformSource
{
    private readonly BuddyVisualTransform[] _transforms =
        new BuddyVisualTransform[PuppetRigProfile.RequiredPartCount];
    private readonly float[] _radii = new float[PuppetRigProfile.RequiredPartCount];
    private readonly string _face;

    public StaticBuddyVisualTransformSource(
        PuppetRigProfile trustedRigProfile,
        Vector2 origin,
        string face = ":|")
    {
        if (!GodotObject.IsInstanceValid(trustedRigProfile))
        {
            throw new InvalidOperationException(
                "StaticBuddyVisualTransformSource requires a trusted rig profile.");
        }

        Godot.Collections.Array<string> errors = trustedRigProfile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid trusted rig profile: {string.Join("; ", errors)}");
        }

        if (!origin.IsFinite())
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Origin must be finite.");
        if (!FaceExpressionCatalog.TryResolve(face, out _))
            throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown semantic face.");

        TrustedRigProfile = trustedRigProfile;
        Origin = origin;
        _face = face;

        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId partId = (BuddyPartId)index;
            PuppetPartDefinition definition = trustedRigProfile.FindPart(partId)
                ?? throw new InvalidOperationException($"Trusted rig is missing {partId}.");
            _radii[index] = definition.Radius;
            _transforms[index] = new BuddyVisualTransform(
                origin + definition.RestPosition,
                0.0f,
                Vector2.Zero);
        }
    }

    public PuppetRigProfile TrustedRigProfile { get; }
    public Vector2 Origin { get; }
    public string Face => _face;

    public BuddyVisualTransform ReadTransform(BuddyPartId partId) =>
        _transforms[CheckedPartIndex(partId)];

    public float ReadRadius(BuddyPartId partId) =>
        _radii[CheckedPartIndex(partId)];

    public string ReadFace() => _face;

    public float ReadFaceDrawRotation() => 0.0f;

    private static int CheckedPartIndex(BuddyPartId partId)
    {
        int index = (int)partId;
        if (index < 0 || index >= PuppetRigProfile.RequiredPartCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(partId),
                partId,
                "Unknown buddy part.");
        }

        return index;
    }
}
