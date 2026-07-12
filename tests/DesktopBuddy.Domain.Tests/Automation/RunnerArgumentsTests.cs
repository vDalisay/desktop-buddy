using System;
using DesktopBuddy.Domain.Automation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Automation;

public sealed class RunnerArgumentsTests
{
    [Fact]
    public void Parse_NoArgs_IsNormalModeWithoutAutomation()
    {
        RunnerArguments result = RunnerArguments.Parse(Array.Empty<string>());

        Assert.Equal(RunnerMode.Normal, result.Mode);
        Assert.Null(result.ScenarioId);
        Assert.Null(result.JourneyId);
        Assert.Null(result.Seed);
        Assert.Null(result.ArtifactsDir);
        Assert.False(result.AutomationRequested);
        Assert.False(result.AutomationEnabled);
    }

    [Fact]
    public void Parse_Scenario_SetsScenarioModeSeedAndEnablesAutomation()
    {
        RunnerArguments result = RunnerArguments.Parse(new[] { "--scenario=boot_smoke", "--seed=42" });

        Assert.Equal(RunnerMode.Scenario, result.Mode);
        Assert.Equal("boot_smoke", result.ScenarioId);
        Assert.Equal(42UL, result.Seed);
        Assert.True(result.AutomationEnabled);
    }

    [Fact]
    public void Parse_Journey_SetsJourneyModeAndArtifacts()
    {
        RunnerArguments result = RunnerArguments.Parse(
            new[] { "--journey=boot_smoke", "--seed=7", @"--artifacts=C:\out\run1" });

        Assert.Equal(RunnerMode.Journey, result.Mode);
        Assert.Equal("boot_smoke", result.JourneyId);
        Assert.Equal(7UL, result.Seed);
        Assert.Equal(@"C:\out\run1", result.ArtifactsDir);
        Assert.True(result.AutomationEnabled);
    }

    [Fact]
    public void Parse_AutomationFlagAlone_IsNormalModeButAutomationEnabled()
    {
        RunnerArguments result = RunnerArguments.Parse(new[] { "--automation" });

        Assert.Equal(RunnerMode.Normal, result.Mode);
        Assert.True(result.AutomationRequested);
        Assert.True(result.AutomationEnabled);
    }

    [Fact]
    public void Parse_IgnoresUnknownAndNonFlagTokens()
    {
        RunnerArguments result = RunnerArguments.Parse(
            new[] { "positional", "--unknown=1", "--verbose", "--scenario=settle" });

        Assert.Equal(RunnerMode.Scenario, result.Mode);
        Assert.Equal("settle", result.ScenarioId);
    }

    [Fact]
    public void Parse_MaxSeed_RoundTrips()
    {
        RunnerArguments result = RunnerArguments.Parse(new[] { $"--seed={ulong.MaxValue}" });

        Assert.Equal(ulong.MaxValue, result.Seed);
    }

    [Fact]
    public void Parse_ScenarioAndJourneyTogether_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RunnerArguments.Parse(new[] { "--scenario=a", "--journey=b" }));
    }

    [Theory]
    [InlineData("--seed=abc")]
    [InlineData("--seed=-1")]
    [InlineData("--seed=3.5")]
    [InlineData("--seed=")]
    public void Parse_InvalidSeed_Throws(string arg)
    {
        Assert.Throws<ArgumentException>(() => RunnerArguments.Parse(new[] { arg }));
    }

    [Theory]
    [InlineData("--scenario=")]
    [InlineData("--journey=")]
    [InlineData("--artifacts=")]
    public void Parse_EmptyRequiredValue_Throws(string arg)
    {
        Assert.Throws<ArgumentException>(() => RunnerArguments.Parse(new[] { arg }));
    }

    [Fact]
    public void Parse_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RunnerArguments.Parse(null!));
    }

    [Fact]
    public void Parse_DevelopmentPathsAndProfiles_RoundTrip()
    {
        RunnerArguments result = RunnerArguments.Parse(new[]
        {
            "--trace-out=trace.json", "--profile-a=rig-a.tres", "--profile-b=rig-b.tres",
            "--drive-a=drive-a.tres", "--drive-b=drive-b.tres"
        });
        Assert.Equal("trace.json", result.TraceOut);
        Assert.Equal("rig-a.tres", result.ProfileA);
        Assert.Equal("rig-b.tres", result.ProfileB);
        Assert.Equal("drive-a.tres", result.DriveA);
        Assert.Equal("drive-b.tres", result.DriveB);
        Assert.True(result.AutomationRequested);
    }

    [Fact]
    public void Parse_PromotionPair_RoundTripsAndRequestsAutomation()
    {
        RunnerArguments result = RunnerArguments.Parse(new[] { "--promote-trace=in.json", "--journey-out=out.json" });
        Assert.Equal("in.json", result.PromoteTrace);
        Assert.Equal("out.json", result.JourneyOut);
        Assert.True(result.AutomationRequested);
    }

    [Theory]
    [InlineData("--promote-trace=in.json")]
    [InlineData("--journey-out=out.json")]
    public void Parse_IncompletePromotionPair_Throws(string argument) =>
        Assert.Throws<ArgumentException>(() => RunnerArguments.Parse(new[] { argument }));
}
