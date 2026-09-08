using System;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.UI;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Presentation facade for the first-session guide. It owns expressive dialogue hosts, reveal
/// interaction, speech-to-mouth coordination and the compact Work portrait host. Tutorial progress,
/// variants, spotlights and action gates remain in <see cref="FirstSessionGuidanceController"/>.
/// </summary>
internal sealed partial class TutorialGuideView : Node
{
    private readonly ITutorialCharacterPresenter? _portraitPresenter;
    private ExpressiveTextPresenter? _mainText;
    private ExpressiveTextPresenter? _workText;

    public TutorialGuideView(ITutorialCharacterPresenter? portraitPresenter)
    {
        _portraitPresenter = portraitPresenter;
        ProcessMode = ProcessModeEnum.Always;
    }

    public bool MainIsRevealing => GodotObject.IsInstanceValid(_mainText) && _mainText!.IsRevealing;

    public void PresentMain(
        Label fallback,
        string identity,
        string semanticMarkup,
        LocalSettingsSave settings)
    {
        ExpressiveTextPresenter presenter = EnsurePresenter(
            fallback,
            ref _mainText,
            "TutorialExpressiveBody");
        presenter.Present(identity, semanticMarkup, settings);
        presenter.Visible = true;
        fallback.Visible = false;

        if (GodotObject.IsInstanceValid(_workText))
            _workText!.Visible = false;
        if (_portraitPresenter is LiveTutorialBuddyPresenter live)
            live.DismissWork();
    }

    public void PresentWork(
        Label fallback,
        Control portraitHost,
        string identity,
        string stepId,
        string semanticMarkup,
        LocalSettingsSave settings)
    {
        ExpressiveTextPresenter presenter = EnsurePresenter(
            fallback,
            ref _workText,
            "TutorialWorkExpressiveBody");
        presenter.Present(identity, semanticMarkup, settings);
        presenter.Visible = true;
        fallback.Visible = false;

        if (GodotObject.IsInstanceValid(_mainText))
            _mainText!.Visible = false;
        if (_portraitPresenter is LiveTutorialBuddyPresenter live)
            live.PresentWork(portraitHost, stepId);
    }

    public bool CompleteMainReveal() =>
        GodotObject.IsInstanceValid(_mainText) && _mainText!.CompleteReveal();

    public void Hide()
    {
        if (GodotObject.IsInstanceValid(_mainText))
            _mainText!.Visible = false;
        if (GodotObject.IsInstanceValid(_workText))
            _workText!.Visible = false;
        if (_portraitPresenter is LiveTutorialBuddyPresenter live)
        {
            live.SetSpeaking(false);
            live.DismissWork();
        }
    }

    private ExpressiveTextPresenter EnsurePresenter(
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
        created.GuiInput += input => OnTextGuiInput(created, input);
        created.SpeakingChanged += OnSpeakingChanged;
        fallback.Visible = false;
        return created;
    }

    private void OnSpeakingChanged(bool speaking)
    {
        if (_portraitPresenter is LiveTutorialBuddyPresenter live)
            live.SetSpeaking(speaking);
    }

    private static void OnTextGuiInput(ExpressiveTextPresenter presenter, InputEvent input)
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
        presenter.AcceptEvent();
    }
}
