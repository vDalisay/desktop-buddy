using System;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.UI;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Expressive-text integration kept in a partial so the action-driven tutorial controller remains
/// the sole owner of progression, spotlighting and input gates. This layer only replaces the two
/// walkthrough body labels with the reusable RichTextLabel presenter after those labels exist.
/// Context Help deliberately keeps its immediate, non-animated Labels.
/// </summary>
public partial class FirstSessionGuidanceController
{
    private TutorialExpressiveBridge? _expressiveBridge;
    private ExpressiveTextPresenter? _expressiveBody;
    private ExpressiveTextPresenter? _expressiveWorkBody;

    private void InstallExpressiveTextBridge()
    {
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
    /// Called after the controller's own process pass. The hidden fallback Labels keep receiving
    /// TextFor output, so conditional tutorial copy and future legacy fallbacks remain intact.
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

        bool useWorkSurface = IsWorkTutorialStep(stepId) &&
                              IsWorkActive() &&
                              GodotObject.IsInstanceValid(_workGuideWindow) &&
                              GodotObject.IsInstanceValid(_workGuideBody) &&
                              _workGuideWindow!.Visible;

        if (useWorkSurface)
        {
            ExpressiveTextPresenter presenter = EnsureExpressivePresenter(
                _workGuideBody!, ref _expressiveWorkBody, "TutorialWorkExpressiveBody");
            PresentInto(presenter, $"work:{stepId}", stepId, _workGuideBody!.Text);
            presenter.Visible = true;
            _workGuideBody.Visible = false;
            if (GodotObject.IsInstanceValid(_expressiveBody))
                _expressiveBody!.Visible = false;
        }
        else
        {
            ExpressiveTextPresenter presenter = EnsureExpressivePresenter(
                _body, ref _expressiveBody, "TutorialExpressiveBody");
            PresentInto(presenter, $"main:{stepId}", stepId, _body.Text);
            presenter.Visible = true;
            _body.Visible = false;
            if (GodotObject.IsInstanceValid(_expressiveWorkBody))
                _expressiveWorkBody!.Visible = false;
        }

        // Continue/Goodbye must never turn the same click that finishes a line into progression.
        // Action-driven steps do not show this button and remain playable while text is revealing.
        if (GodotObject.IsInstanceValid(_dismiss) && _dismiss.Visible)
            _dismiss.Disabled = _expressiveBody is { IsRevealing: true };
        else if (GodotObject.IsInstanceValid(_dismiss))
            _dismiss.Disabled = false;
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
        presenter = new ExpressiveTextPresenter
        {
            Name = name,
            CustomMinimumSize = fallback.CustomMinimumSize,
            SizeFlagsHorizontal = fallback.SizeFlagsHorizontal,
            SizeFlagsVertical = fallback.SizeFlagsVertical,
            Visible = false,
        };
        parent.AddChild(presenter);
        parent.MoveChild(presenter, fallbackIndex);

        // _Ready intentionally defaults reusable dialogue to Ignore. Tutorial text is one of the
        // few surfaces where clicking the words themselves has meaning: reveal the rest now.
        presenter.MouseFilter = Control.MouseFilterEnum.Stop;
        presenter.GuiInput += input => OnExpressiveBodyGuiInput(presenter, input);
        fallback.Visible = false;
        return presenter;
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
    }
}

/// <summary>
/// Runs immediately after its parent controller in tree order. It also catches clicks on the
/// empty parts of the main tutorial frame so the first click there completes the typewriter.
/// The separate Work window is covered by the presenter's GuiInput handler above.
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
