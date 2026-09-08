using System;
using System.Text.Json;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class WorkProgressSavePolicyTests
{
    [Fact]
    public void Round_trip_preserves_lifetime_and_active_session_state()
    {
        var state = new WorkProgressState(
            lifetime: new WorkCounterSnapshot(12_345, 6_789),
            claimedLifetimeMilestoneIds: ["employee.day", "keyboard.10k"],
            firstEntryGlassesGranted: true,
            revision: 9,
            activeSession: new WorkSessionSnapshot(
                Guid.Parse("6df807e3-86f1-47c1-8adb-4a63bf5ea7bb"),
                new WorkCounterSnapshot(321, 123),
                ["session.actions.100"]));

        string json = WorkProgressSavePolicy.Serialize(state);
        WorkProgressDecodeResult decoded = WorkProgressSavePolicy.Decode(json);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.NotNull(decoded.State);
        WorkProgressSnapshot restored = decoded.State!.Snapshot();
        Assert.Equal(9, restored.Revision);
        Assert.Equal(12_345, restored.Lifetime.KeyboardPresses);
        Assert.Equal(6_789, restored.Lifetime.MouseClicks);
        Assert.True(restored.FirstEntryGlassesGranted);
        Assert.Contains("employee.day", restored.ClaimedLifetimeMilestoneIds);
        Assert.Contains("keyboard.10k", restored.ClaimedLifetimeMilestoneIds);
        Assert.True(restored.ActiveSession.HasValue);
        Assert.Equal(321, restored.ActiveSession.Value.Counters.KeyboardPresses);
        Assert.Equal(123, restored.ActiveSession.Value.Counters.MouseClicks);
        Assert.Contains("session.actions.100", restored.ActiveSession.Value.EarnedRepeatPerSessionMilestoneIds);
    }

    [Fact]
    public void Duplicate_or_blank_milestone_ids_are_rejected()
    {
        var duplicate = new WorkProgressDocumentSave
        {
            Progress = new WorkProgressSave
            {
                ClaimedLifetimeMilestoneIds = ["same", "same"],
            },
        };
        var blankSession = new WorkProgressDocumentSave
        {
            Progress = new WorkProgressSave
            {
                ActiveSession = new WorkSessionSave
                {
                    SessionId = Guid.NewGuid(),
                    EarnedRepeatPerSessionMilestoneIds = [""],
                },
            },
        };

        AssertInvalid(duplicate);
        AssertInvalid(blankSession);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Malformed_payloads_are_reported_as_malformed(string json)
    {
        Assert.Equal(SaveDecodeStatus.Malformed, WorkProgressSavePolicy.Decode(json).Status);
    }

    [Fact]
    public void Future_schema_is_unsupported_not_treated_as_corruption()
    {
        WorkProgressDecodeResult decoded = WorkProgressSavePolicy.Decode("{\"schemaVersion\":999}");

        Assert.Equal(SaveDecodeStatus.UnsupportedFutureVersion, decoded.Status);
        Assert.Null(decoded.State);
    }

    private static void AssertInvalid(WorkProgressDocumentSave save)
    {
        string json = JsonSerializer.Serialize(
            save,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(SaveDecodeStatus.Invalid, WorkProgressSavePolicy.Decode(json).Status);
    }
}
