using System;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class SceneSwitchTransactionTests
{
    [Fact]
    public void Safe_switch_requires_the_owner_locked_nine_steps_in_order()
    {
        SceneId outgoing = Id(1);
        SceneId target = Id(2);
        var transaction = new SceneSwitchTransaction(outgoing, target);
        SceneSwitchStep[] expected =
        [
            SceneSwitchStep.ResolveModalState,
            SceneSwitchStep.CaptureBuddyAnchors,
            SceneSwitchStep.PersistOutgoingScene,
            SceneSwitchStep.TearDownActiveRuntime,
            SceneSwitchStep.SwitchEnvironment,
            SceneSwitchStep.InstantiateTargetRuntime,
            SceneSwitchStep.ApplyAppearanceAndPaint,
            SceneSwitchStep.RestoreInput,
            SceneSwitchStep.ActivateTargetScene,
        ];

        Assert.Equal(expected, transaction.RequiredSteps);
        foreach (SceneSwitchStep step in expected)
        {
            Assert.Equal(step, transaction.NextRequiredStep);
            transaction.CompleteStep(step);
        }

        Assert.True(transaction.IsComplete);
        Assert.Equal(SceneSwitchCompletion.Completed, transaction.Completion);
        Assert.Null(transaction.NextRequiredStep);
        Assert.Equal(target, transaction.CommittedActiveSceneId);
        Assert.Equal(target, transaction.RuntimeSceneId);
    }

    [Fact]
    public void Out_of_order_completion_is_rejected_without_advancing_transaction()
    {
        var transaction = new SceneSwitchTransaction(Id(1), Id(2));

        Assert.Throws<InvalidOperationException>(() =>
            transaction.CompleteStep(SceneSwitchStep.PersistOutgoingScene));

        Assert.Equal(SceneSwitchStep.ResolveModalState, transaction.NextRequiredStep);
        Assert.Equal(Id(1), transaction.CommittedActiveSceneId);
        Assert.Equal(Id(1), transaction.RuntimeSceneId);
    }

    [Fact]
    public void Teardown_creates_a_real_no_runtime_gap_until_target_instantiation()
    {
        SceneId outgoing = Id(1);
        SceneId target = Id(2);
        var transaction = new SceneSwitchTransaction(outgoing, target);
        CompleteThrough(transaction, SceneSwitchStep.TearDownActiveRuntime);

        Assert.False(transaction.HasLiveRuntime);
        Assert.False(transaction.RuntimeSceneId.IsValid);
        Assert.Equal(outgoing, transaction.CommittedActiveSceneId);
        Assert.Equal(SceneSwitchStep.SwitchEnvironment, transaction.NextRequiredStep);

        transaction.CompleteStep(SceneSwitchStep.SwitchEnvironment);
        Assert.False(transaction.HasLiveRuntime);
        transaction.CompleteStep(SceneSwitchStep.InstantiateTargetRuntime);

        Assert.True(transaction.HasLiveRuntime);
        Assert.Equal(target, transaction.RuntimeSceneId);
        Assert.Equal(outgoing, transaction.CommittedActiveSceneId);
    }

    [Fact]
    public void Active_identity_does_not_change_until_final_activation()
    {
        SceneId outgoing = Id(1);
        SceneId target = Id(2);
        var transaction = new SceneSwitchTransaction(outgoing, target);
        CompleteThrough(transaction, SceneSwitchStep.RestoreInput);

        Assert.Equal(target, transaction.RuntimeSceneId);
        Assert.Equal(outgoing, transaction.CommittedActiveSceneId);
        Assert.False(transaction.IsComplete);

        transaction.CompleteStep(SceneSwitchStep.ActivateTargetScene);

        Assert.Equal(target, transaction.CommittedActiveSceneId);
        Assert.True(transaction.IsComplete);
    }

    [Fact]
    public void Failed_external_step_can_be_retried_without_lying_about_active_scene()
    {
        SceneId outgoing = Id(1);
        var transaction = new SceneSwitchTransaction(outgoing, Id(2));
        transaction.CompleteStep(SceneSwitchStep.ResolveModalState);
        transaction.CompleteStep(SceneSwitchStep.CaptureBuddyAnchors);

        // Persistence failed externally: do not acknowledge it.
        Assert.Equal(SceneSwitchStep.PersistOutgoingScene, transaction.NextRequiredStep);
        Assert.Equal(outgoing, transaction.CommittedActiveSceneId);
        Assert.Equal(outgoing, transaction.RuntimeSceneId);

        // Retry succeeds later and can then advance normally.
        transaction.CompleteStep(SceneSwitchStep.PersistOutgoingScene);
        Assert.Equal(SceneSwitchStep.TearDownActiveRuntime, transaction.NextRequiredStep);
    }

    [Fact]
    public void Same_or_invalid_scene_ids_cannot_begin_a_switch()
    {
        SceneId scene = Id(1);
        Assert.Throws<ArgumentException>(() => new SceneSwitchTransaction(default, scene));
        Assert.Throws<ArgumentException>(() => new SceneSwitchTransaction(scene, default));
        Assert.Throws<ArgumentException>(() => new SceneSwitchTransaction(scene, scene));

        // A cast change recomposes the same room and is the one legitimate same-Scene transaction.
        var reload = new SceneSwitchTransaction(scene, scene, reloadInPlace: true);
        Assert.Equal(scene, reload.TargetSceneId);
    }

    private static void CompleteThrough(SceneSwitchTransaction transaction, SceneSwitchStep finalStep)
    {
        while (transaction.NextRequiredStep is SceneSwitchStep step)
        {
            transaction.CompleteStep(step);
            if (step == finalStep)
                return;
        }
        throw new InvalidOperationException("Requested switch step was not part of the transaction.");
    }

    private static SceneId Id(int value) =>
        SceneId.From(Guid.Parse($"00000000-0000-0000-0000-{value:D12}"));
}
