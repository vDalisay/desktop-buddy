using System;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Immutable identity/value snapshot of every trusted geometry input owned by a visual rig.
/// Appearance tests capture this before and after mutations to prove that customization did
/// not rebuild or replace geometry, tuning, the profile, or the transform source.
/// </summary>
public readonly record struct BuddyVisualRigTrustSnapshot(
    BuddyVisualProfile Profile,
    IBuddyVisualTransformSource GeometrySource,
    BuddyLookProfile LookProfile,
    Mesh HeadMesh,
    Mesh TorsoMesh,
    Mesh LeftHandMesh,
    Mesh RightHandMesh,
    Mesh LeftFootMesh,
    Mesh RightFootMesh,
    float HeadRadius,
    float TorsoRadius,
    float LeftHandRadius,
    float RightHandRadius,
    float LeftFootRadius,
    float RightFootRadius,
    int SocketCount,
    int ConnectorCount,
    ulong ConnectorMeshIdentityHash,
    ulong PartDefinitionIdentityHash,
    ulong ConnectorDefinitionIdentityHash,
    float CapsuleHeightScale,
    float ConnectorMinimumLength);

public partial class BuddyVisualRigView
{
    public BuddyVisualRigTrustSnapshot CaptureTrustSnapshot()
    {
        EnsureInitialized();
        return new BuddyVisualRigTrustSnapshot(
            _trustedProfile,
            _geometrySource,
            _trustedProfile.Look,
            RequiredMesh(BuddyPartId.Head),
            RequiredMesh(BuddyPartId.Torso),
            RequiredMesh(BuddyPartId.LeftHand),
            RequiredMesh(BuddyPartId.RightHand),
            RequiredMesh(BuddyPartId.LeftFoot),
            RequiredMesh(BuddyPartId.RightFoot),
            _meshRadii[(int)BuddyPartId.Head],
            _meshRadii[(int)BuddyPartId.Torso],
            _meshRadii[(int)BuddyPartId.LeftHand],
            _meshRadii[(int)BuddyPartId.RightHand],
            _meshRadii[(int)BuddyPartId.LeftFoot],
            _meshRadii[(int)BuddyPartId.RightFoot],
            _sockets.Length,
            _connectorMeshes.Length,
            IdentityHash(_connectorMeshes),
            IdentityHash(_partDefinitions),
            IdentityHash(_connectorDefinitions),
            _trustedProfile.CapsuleHeightScale,
            _trustedProfile.ConnectorMinimumLength);
    }

    public bool TrustedGeometryMatches(in BuddyVisualRigTrustSnapshot expected)
    {
        if (!IsInitialized ||
            !ReferenceEquals(expected.Profile, _trustedProfile) ||
            !ReferenceEquals(expected.GeometrySource, _geometrySource) ||
            !ReferenceEquals(expected.LookProfile, _trustedProfile.Look))
        {
            return false;
        }

        BuddyVisualRigTrustSnapshot current = CaptureTrustSnapshot();
        return current == expected;
    }

    private Mesh RequiredMesh(BuddyPartId partId) =>
        _partMeshes[(int)partId].Mesh
        ?? throw new InvalidOperationException($"Visual mesh for {partId} is missing.");

    private static ulong IdentityHash(MeshInstance3D[] values)
    {
        ulong hash = 1469598103934665603UL;
        for (int index = 0; index < values.Length; index++)
        {
            Mesh mesh = values[index].Mesh
                ?? throw new InvalidOperationException($"Connector mesh {index} is missing.");
            hash = Mix(hash, mesh.GetInstanceId());
        }

        return hash;
    }

    private static ulong IdentityHash(PartVisualDefinition[] values)
    {
        ulong hash = 1469598103934665603UL;
        for (int index = 0; index < values.Length; index++)
            hash = Mix(hash, values[index].GetInstanceId());
        return hash;
    }

    private static ulong IdentityHash(ConnectorVisualDefinition[] values)
    {
        ulong hash = 1469598103934665603UL;
        for (int index = 0; index < values.Length; index++)
            hash = Mix(hash, values[index].GetInstanceId());
        return hash;
    }

    private static ulong Mix(ulong hash, ulong value) =>
        (hash ^ value) * 1099511628211UL;
}
