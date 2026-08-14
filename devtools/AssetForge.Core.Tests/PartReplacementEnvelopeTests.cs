using System.Numerics;
using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementEnvelopeTests
{
    [Fact]
    public void Warns_only_when_visual_substantially_exceeds_trusted_radius()
    {
        var inside = MeshAtRadius(1.20f);
        var outside = MeshAtRadius(1.30f);

        PartReplacementEnvelopeDiagnostic accepted = PartReplacementEnvelopeDiagnostics.Analyze(inside);
        PartReplacementEnvelopeDiagnostic warning = PartReplacementEnvelopeDiagnostics.Analyze(outside);

        Assert.False(accepted.SubstantiallyExceedsPhysicsEnvelope);
        Assert.True(warning.SubstantiallyExceedsPhysicsEnvelope);
        Assert.Contains("Physics remains unchanged", warning.Summary, StringComparison.Ordinal);
    }

    private static CanonicalMesh MeshAtRadius(float radius)
    {
        var mesh = new CanonicalMesh();
        uint a = mesh.AddVertex(new Vector3(radius, 0, 0), Vector2.Zero);
        uint b = mesh.AddVertex(new Vector3(0, radius, 0), Vector2.One);
        uint c = mesh.AddVertex(new Vector3(-radius, 0, 0), new Vector2(0, 1));
        mesh.AddTriangle(a, b, c);
        mesh.RecalculateNormals();
        return mesh;
    }
}
