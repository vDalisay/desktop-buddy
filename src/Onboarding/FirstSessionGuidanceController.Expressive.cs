using System;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.UI;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Expressive-guide integration kept in a partial so the action-driven tutorial controller remains
/// the sole owner of progression, spotlighting and input gates. This layer owns semantic
/// presentation identity, the two RichText dialogue hosts, and live portrait coordination.
/// Context Help deliberately keeps its immediate, non-animated Labels.
/// </summary>
public partial class FirstSessionGuidanceController
{
    private TutorialSemanticRefreshBridge? _semanticRefreshBridge;
    private TutorialExpressiveBridge? _expressiveBridge;
    private ExpressiveTextPresenter? _expressiveBody;
    private ExpressiveTextPresenter? _expressiveWorkBody;
    private Control? _workGuidePortraitHost;
    private string? _semanticPresentationIdentity;

    private void InstallExpressiveTextBridge()
    {
        if (!GodotObject.IsInstanceValid(_semanticRefreshBridge))
        {
            _semanticRefreshBridge = new TutorialSemanticRefreshBridge(this)
            {
                Name = nameof(TutorialSemanticRefreshBridge),
                ProcessMode = ProcessModeEnum.Always,
                // Run before the controller's default priority so the old rendered-text guard is
                // made inert before FirstSessionGuidanceController._Process evaluates it.
                ProcessPriority = -100,
            };
            AddChild(_semanticRefreshBridge);
        }

        if (GodotObject.IsInstanceValid(_expressiveBridge))
            return;

        _expressiveBridge = new TutorialExpressiveBridge(this)
        {
            Name = nameof(TutorialExpressiveBridge),
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(_expressiveBridge);
    }

    /// <summary>
    /// Semantic step+variant identity is the refresh authority for conditional tutorial copy.
    /// The controller still contains its pre-feature rendered-text guard, but this pass mirrors the
    /// current source into that legacy field before the controller processes, so text equality can
    /// no longer decide whether a variant re-renders. This also makes a live Drop Tool rebind a
    /// semantic variant rather than depending on English source-string changes.
    /// </summary>
    internal void PrepareSemanticTutorialRefresh()
    {
        if (_tutorial is null)
            return;

        string? stepId = _tutorial.NextIncompleteStepId;
        string? identity = stepId is null ? null : ResolveTutorialPresentationIdentity(stepId);
        if (stepId is not null &&
            string.Equals(stepId, _displayedStepId, StringComparison.Ordinal) &&
            !string.Equals(identity, _semanticPresentationIdentity, StringComparison.Ordinal))
        {
            RefreshHint();
        }

        _semanticPresentationIdentity = identity;
        // Neutralize the legacy text-equality refresh path. Step changes are still handled by the
        // controller's step-id comparison; live variants are handled by identity above.
        _lastRenderedText = stepId is null ? null : TextFor(stepId);
    }

    /// <summary>
    /// Called after the controller's own process pass. The hidden fallback Labels keep receiving
    /// TextFor output for non-expressive fallback/readability, while the visible walkthrough uses
    /// semantic identity and one reusable RichText presenter per host.
    /// </summary>
    internal void SyncExpressiveTutorialPresentation()
    {
        if (!GodotObject.IsInstanceValid(_body) || !GodotObject.IsInstanceValid(_panel))
            return;

        string? stepId = _displayedStepId;
        if (_helpActive || stepId is null)
        {
            HideExpressivePresenters();
            if (GodotObject.IsInstanceValid(_dismiss))
                _dismiss.Disabled = false;
            return;
        }

        _semanticPresentationIdentity = ResolveTutorialPresentationIdentity(stepId);
        bool useWorkSurface = IsWorkTutorialStep(stepId) &&
                              IsWorkActive() &&
                              GodotObject.IsInstanceValid(_workGuideWindow) &&
                              GodotObject.IsInstanceValid(_workGuideBody) &&
                              _workGuideWindow!.Visible;

        if (useWorkSurface)
        {
            ExpressiveTextPresenter presenter = EnsureExpressivePresenter(
                _workGuideBody!, ref _expressiveWorkBody, "TutorialWorkExpressiveBody");
            PresentInto(
                presenter,
                $"work:{_semanticPresentationIdentity}",
                stepId,
                _workGuideBody!.Text);
            presenter.Visible = true;
            _workGuideBody.Visible = false;
            if (GodotObject.IsInstanceValid(_expressiveBody))
                _expressiveBody!.Visible = false;

            if (_characterPresenter is LiveTutorialBuddyPresenter live)
                live.PresentWork(EnsureWorkGuidePortraitHost(), stepId);
        }
        else
        {
            ExpressiveTextPresenter presenter = EnsureExpressivePresenter(
                _body, ref _expressiveBody, "TutorialExpressiveBody");
            PresentInto(
                presenter,
                $"main:{_semanticPresentationIdentity}",
                stepId,
                _body.Text);
            presenter.Visible = true;
            _body.Visible = false;
            if (GodotObject.IsInstanceValid(_expressiveWorkBody))
                _expressiveWorkBody!.Visible = false;
            if (_characterPresenter is LiveTutorialBuddyPresenter live)
                live.DismissWork();
        }

        // Continue/Goodbye must never turn the same click that finishes a line into progression.
        // Action-driven steps do not show this button and remain playable while text is revealing.
        if (GodotObject.IsInstanceValid(_dismiss) && _dismiss.Visible)
            _dismiss.Disabled = _expressiveBody is { IsRevealing: true };
        else if (GodotObject.IsInstanceValid(_dismiss))
            _dismiss.Disabled = false;
    }

    private string ResolveTutorialPresentationIdentity(string stepId)
    {
        string variant = stepId switch
        {
            TutorialStepIds.CreateBuddy => CanCreateCharacter() ? "can-create" : "select-existing",
            TutorialStepIds.ExitBuddyStudio => _studioNothingToSave ? "nothing-to-save" : "saved-item",
            TutorialStepIds.UnequipTool =>
                "drop-" + LocalSettingsInputBindings.DropTool(_sandbox.Shell.CurrentLocalSettings),
            _ => "default",
        };
        return $"{stepId}:{variant}";
    }

    internal bool CompleteMainExpressiveRevealAt(Vector2 viewportPosition)
    {
        if (!GodotObject.IsInstanceValid(_panel) || !_panel.Visible ||
            !_panel.GetGlobalRect().HasPoint(viewportPosition) ||
            !GodotObject.IsInstanceValid(_expressiveBody))
        {
            return false;
        }

        bool completed = _expressiveBody!.CompleteReveal();
        if (completed && GodotObject.IsInstanceValid(_dismiss))
            _dismiss.Disabled = false;
        return completed;
    }

    private void PresentInto(
        ExpressiveTextPresenter presenter,
        string identity,
        string stepId,
        string plainText)
    {
        string dropBinding = LocalSettingsInputBindings.DropTool(_sandbox.Shell.CurrentLocalSettings);
        string semantic = TutorialExpressiveCopy.Format(stepId, plainText, dropBinding);
        presenter.Present(identity, semantic, _sandbox.Shell.CurrentLocalSettings);
    }

    private ExpressiveTextPresenter EnsureExpressivePresenter(
        Label fallback,
        ref ExpressiveTextPresenter? presenter,
        string name)
    {
        if (GodotObject.IsInstanceValid(presenter))
            return presenter!;

        Node parent = fallback.GetParent()
            ?? throw new InvalidOperationException($"Tutorial body '{fallback.Name}' has no parent.");
        int fallbackIndex = fallback.GetIndex();
        var created = new ExpressiveTextPresenter
        {
            Name = name,
            CustomMinimumSize = fallback.CustomMinimumSize,
            SizeFlagsHorizontal = fallback.SizeFlagsHorizontal,
            SizeFlagsVertical = fallback.SizeFlagsVertical,
            Visible = false,
        };
        presenter = created;
        parent.AddChild(created);
        parent.MoveChild(created, fallbackIndex);

        created.MouseFilter = Control.MouseFilterEnum.Stop;
        created.GuiInput += input => OnExpressiveBodyGuiInput(created, input);
        created.SpeakingChanged += OnExpressiveSpeakingChanged;
        fallback.Visible = false;
        return created;
    }

    private Control EnsureWorkGuidePortraitHost()
    {
        if (GodotObject.IsInstanceValid(_workGuidePortraitHost))
            return _workGuidePortraitHost!;
        if (!GodotObject.IsInstanceValid(_workGuideBody) ||
            _workGuideBody!.GetParent()?.GetParent() is not HBoxContainer split)
        {
            throw new InvalidOperationException("Work tutorial guide split was not composed.");
        }

        _workGuidePortraitHost = new Control
        {
            Name = "TutorialWorkGuideSlot",
            CustomMinimumSize = new Vector2(92, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        split.AddChild(_workGuidePortraitHost);
        return _workGuidePortraitHost;
    }

    private void OnExpressiveSpeakingChanged(bool speaking)
    {
        if (_characterPresenter is LiveTutorialBuddyPresenter live)
            live.SetSpeaking(speaking);
    }

    private void OnExpressiveBodyGuiInput(ExpressiveTextPresenter presenter, InputEvent input)
    {
        if (input is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            })
        {
            return;
        }

        if (!presenter.CompleteReveal())
            return;

        if (ReferenceEquals(presenter, _expressiveBody) && GodotObject.IsInstanceValid(_dismiss))
            _dismiss.Disabled = false;
        presenter.AcceptEvent();
    }

    private void HideExpressivePresenters()
    {
        if (GodotObject.IsInstanceValid(_expressiveBody))
            _expressiveBody!.Visible = false;
        if (GodotObject.IsInstanceValid(_expressiveWorkBody))
            _expressiveWorkBody!.Visible = false;
        if (_characterPresenter is LiveTutorialBuddyPresenter live)
        {
            live.SetSpeaking(false);
            live.DismissWork();
        }
    }
}

/// <summary>Pre-controller semantic variant refresh; see PrepareSemanticTutorialRefresh.</summary>
internal sealed partial class TutorialSemanticRefreshBridge : Node
{
    private readonly FirstSessionGuidanceController _owner;

    public TutorialSemanticRefreshBridge(FirstSessionGuidanceController owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public override void _Process(double delta)
    {
        _ = delta;
        if (GodotObject.IsInstanceValid(_owner))
            _owner.PrepareSemanticTutorialRefresh();
    }
}

/// <summary>
/// Runs after its parent controller in normal tree order. It presents the semantic guide and catches
/// clicks on empty parts of the main tutorial frame so the first click completes the typewriter.
/// The separate Work window is covered by the presenter's GuiInput handler.
/// </summary>
internal sealed partial class TutorialExpressiveBridge : Node
{
    private readonly FirstSessionGuidanceController _owner;

    public TutorialExpressiveBridge(FirstSessionGuidanceController owner) =>
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public override void _Process(double delta)
    {
        _ = delta;
        if (GodotObject.IsInstanceValid(_owner))
            _owner.SyncExpressiveTutorialPresentation();
    }

    public override void _Input(InputEvent input)
    {
        if (!GodotObject.IsInstanceValid(_owner) ||
            input is not InputEventMouseButton
            {
                Pressed: true,
                ButtonIndex: MouseButton.Left,
            } mouse)
        {
            return;
        }

        if (_owner.CompleteMainExpressiveRevealAt(mouse.Position))
            GetViewport().SetInputAsHandled();
    }
}
