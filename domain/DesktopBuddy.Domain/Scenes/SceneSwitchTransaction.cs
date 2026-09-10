using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Scenes;

/// <summary>The owner-locked safe Scene switch sequence from the master release plan §6.3.</summary>
public enum SceneSwitchStep
{
    ResolveModalState = 0,
    CaptureBuddyAnchors = 1,
    PersistOutgoingScene = 2,
    TearDownActiveRuntime = 3,
    SwitchEnvironment = 4,
    InstantiateTargetRuntime = 5,
    ApplyAppearanceAndPaint = 6,
    RestoreInput = 7,
    ActivateTargetScene = 8,
}

public enum SceneSwitchCompletion
{
    InProgress = 0,
    Completed,
}

/// <summary>
/// Engine-free transaction guard for switching the one live Scene. It owns no rendering, physics or
/// persistence itself; adapters perform the requested step and acknowledge it here. Acknowledgement
/// is strictly ordered so UI/runtime code cannot accidentally activate a target Scene before the
/// outgoing document has committed and its live runtime has been torn down.
/// </summary>
public sealed class SceneSwitchTransaction
{
    private static readonly SceneSwitchStep[] OrderedSteps =
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

    private int _nextStepIndex;

    /// <param name="reloadInPlace">
    /// True when the active Scene is being recomposed after its own document changed (a cast change),
    /// which is the only case where the outgoing and target Scene are the same room.
    /// </param>
    public SceneSwitchTransaction(SceneId outgoingSceneId, SceneId targetSceneId, bool reloadInPlace = false)
    {
        if (!outgoingSceneId.IsValid)
            throw new ArgumentException("Scene switch requires a stable outgoing Scene ID.", nameof(outgoingSceneId));
        if (!targetSceneId.IsValid)
            throw new ArgumentException("Scene switch requires a stable target Scene ID.", nameof(targetSceneId));
        if (outgoingSceneId == targetSceneId && !reloadInPlace)
            throw new ArgumentException("Scene switch target must differ from the active Scene.", nameof(targetSceneId));

        OutgoingSceneId = outgoingSceneId;
        TargetSceneId = targetSceneId;
        CommittedActiveSceneId = outgoingSceneId;
        RuntimeSceneId = outgoingSceneId;
    }

    public SceneId OutgoingSceneId { get; }
    public SceneId TargetSceneId { get; }

    /// <summary>
    /// Durable/UI active identity. It stays on the outgoing Scene until the final activation step,
    /// so a failed mid-switch never pretends the target is active.
    /// </summary>
    public SceneId CommittedActiveSceneId { get; private set; }

    /// <summary>
    /// Scene whose live runtime currently exists. Invalid after outgoing teardown and before target
    /// instantiation, proving there can never be two hidden live Scene worlds during a switch.
    /// </summary>
    public SceneId RuntimeSceneId { get; private set; }

    public bool HasLiveRuntime => RuntimeSceneId.IsValid;
    public bool IsComplete => _nextStepIndex == OrderedSteps.Length;
    public SceneSwitchCompletion Completion =>
        IsComplete ? SceneSwitchCompletion.Completed : SceneSwitchCompletion.InProgress;
    public SceneSwitchStep? NextRequiredStep =>
        IsComplete ? null : OrderedSteps[_nextStepIndex];
    public IReadOnlyList<SceneSwitchStep> RequiredSteps => OrderedSteps;

    /// <summary>
    /// Acknowledges a successfully completed external operation. Failures must not call this method;
    /// the transaction then remains at the same required step and can be retried or abandoned while
    /// its committed active Scene identity remains truthful.
    /// </summary>
    public void CompleteStep(SceneSwitchStep step)
    {
        if (IsComplete)
            throw new InvalidOperationException("Scene switch transaction is already complete.");
        SceneSwitchStep required = OrderedSteps[_nextStepIndex];
        if (step != required)
        {
            throw new InvalidOperationException(
                $"Scene switch step '{step}' cannot complete while '{required}' is required.");
        }

        switch (step)
        {
            case SceneSwitchStep.TearDownActiveRuntime:
                RuntimeSceneId = default;
                break;
            case SceneSwitchStep.InstantiateTargetRuntime:
                if (RuntimeSceneId.IsValid)
                {
                    throw new InvalidOperationException(
                        "Target Scene cannot instantiate while another Scene runtime is still live.");
                }
                RuntimeSceneId = TargetSceneId;
                break;
            case SceneSwitchStep.ActivateTargetScene:
                if (RuntimeSceneId != TargetSceneId)
                {
                    throw new InvalidOperationException(
                        "Target Scene cannot activate until its runtime has been instantiated.");
                }
                CommittedActiveSceneId = TargetSceneId;
                break;
        }

        _nextStepIndex++;
    }
}
