using System.Linq;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class BuddyLookDataTests
{
    /// <summary>The owner-accepted Variant C defaults, mirrored from lab_buddy_look.tres.</summary>
    private static BuddyLookData Accepted() => new(
        DiffuseMode: 1,
        SpecularMode: 1,
        Specular: 0.08f,
        Roughness: 1.0f,
        KeyColor: new LookColor(1.0f, 0.98f, 0.94f, 1.0f),
        KeyEnergy: 0.75f,
        KeyEulerDegrees: new LookEuler(-35.0f, -30.0f, 0.0f),
        FillColor: new LookColor(0.85f, 0.90f, 1.0f, 1.0f),
        FillEnergy: 0.70f,
        FillEulerDegrees: new LookEuler(0.0f, 0.0f, 0.0f),
        ShadowsEnabled: false,
        OutlineColor: new LookColor(0.094f, 0.188f, 0.259f, 1.0f),
        OutlineGrowAmount: 1.5f);

    [Fact]
    public void AcceptedProfile_PassesWithNoErrors()
    {
        Assert.Empty(Accepted().Validate());
    }

    [Fact]
    public void ShadowsEnabled_IsRejectedForTheAcceptedProfile()
    {
        BuddyLookData look = Accepted() with { ShadowsEnabled = true };

        Assert.Contains(look.Validate(), error => error.Contains("shadows"));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-0.5f)]
    public void NonFiniteOrNegativeKeyEnergy_Fails(float energy)
    {
        BuddyLookData look = Accepted() with { KeyEnergy = energy };

        Assert.Contains(look.Validate(), error => error.Contains("key light energy"));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1.0f)]
    [InlineData(1000.0f)]
    public void OutOfRangeFillEnergy_Fails(float energy)
    {
        BuddyLookData look = Accepted() with { FillEnergy = energy };

        Assert.Contains(look.Validate(), error => error.Contains("fill light energy"));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.5f)]
    [InlineData(float.NaN)]
    public void OutOfRangeSpecular_Fails(float specular)
    {
        BuddyLookData look = Accepted() with { Specular = specular };

        Assert.Contains(look.Validate(), error => error.Contains("specular must be finite"));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(2.0f)]
    [InlineData(float.PositiveInfinity)]
    public void OutOfRangeRoughness_Fails(float roughness)
    {
        BuddyLookData look = Accepted() with { Roughness = roughness };

        Assert.Contains(look.Validate(), error => error.Contains("roughness"));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.5f)]
    [InlineData(float.NaN)]
    public void NonPositiveOrNonFiniteOutlineGrow_Fails(float grow)
    {
        BuddyLookData look = Accepted() with { OutlineGrowAmount = grow };

        Assert.Contains(look.Validate(), error => error.Contains("outline grow"));
    }

    [Fact]
    public void NonFiniteKeyColour_Fails()
    {
        BuddyLookData look = Accepted() with
        {
            KeyColor = new LookColor(float.NaN, 0.98f, 0.94f, 1.0f),
        };

        Assert.Contains(look.Validate(), error => error.Contains("key light colour"));
    }

    [Fact]
    public void NonFiniteOutlineColour_Fails()
    {
        BuddyLookData look = Accepted() with
        {
            OutlineColor = new LookColor(0.094f, float.PositiveInfinity, 0.259f, 1.0f),
        };

        Assert.Contains(look.Validate(), error => error.Contains("outline colour"));
    }

    [Fact]
    public void NonFiniteKeyEuler_Fails()
    {
        BuddyLookData look = Accepted() with
        {
            KeyEulerDegrees = new LookEuler(float.NaN, -30.0f, 0.0f),
        };

        Assert.Contains(look.Validate(), error => error.Contains("key light euler"));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(4, 1)]
    [InlineData(1, -1)]
    [InlineData(1, 3)]
    public void OutOfRangeMaterialModes_Fail(int diffuseMode, int specularMode)
    {
        BuddyLookData look = Accepted() with
        {
            DiffuseMode = diffuseMode,
            SpecularMode = specularMode,
        };

        Assert.NotEmpty(look.Validate());
    }

    [Fact]
    public void MultipleBadFields_ReportEveryActionableName()
    {
        BuddyLookData look = Accepted() with
        {
            KeyEnergy = float.NaN,
            Roughness = 5.0f,
            OutlineGrowAmount = -1.0f,
            ShadowsEnabled = true,
        };

        var errors = look.Validate().ToList();
        Assert.Contains(errors, error => error.Contains("key light energy"));
        Assert.Contains(errors, error => error.Contains("roughness"));
        Assert.Contains(errors, error => error.Contains("outline grow"));
        Assert.Contains(errors, error => error.Contains("shadows"));
    }
}
