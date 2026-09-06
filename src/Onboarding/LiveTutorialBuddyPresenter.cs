using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Presentation-only live Buddy guide for the first-session tutorial. It uses the same
/// physics-free <see cref="BuddyPreviewSurface"/> as Work, Character Editor and Workshop capture,
/// copies only the live Buddy's appearance/paint, and owns its own blink/mouth state. No gameplay
/// body, reaction component, autonomy state or clock is ever shared with the portrait.
/// </summary>
public sealed class LiveTutorialBuddyPresenter : ITutorialCharacterPresenter
{
    private readonly FirstSessionGuidanceController _owner;
    private readonly SandboxRoot _sandbox;
    private TutorialBuddyPortraitCard? _card;

    public LiveTutorialBuddyPresenter(FirstSessionGuidanceController owner, SandboxRoot sandbox)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
    }

    public void Present(string stepId, string text)
    {
        _ = text;
        if (!GodotObject.IsInstanceValid(_owner) || !_owner.IsInsideTree() ||
            !GodotObject.IsInstanceValid(_owner.GuideSlot))
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(_card))
        {
            _card = new TutorialBuddyPortraitCard(_sandbox)
            {
                Name = "LiveTutorialBuddy",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            _owner.GuideSlot.AddChild(_card);
        }

        _card!.SetStep(stepId);
        _card.Visible = true;
    }

    public void Dismiss()
    {
        if (GodotObject.IsInstanceValid(_card))
        {
            _card!.SetSpeaking(false);
            _card.Visible = false;
        }
    }

    public void SetSpeaking(bool speaking)
    {
        if (GodotObject.IsInstanceValid(_card))
            _card!.SetSpeaking(speaking);
    }

    private enum TutorialPortraitMood
    {
        Neutral = 0,
        Friendly = 1,
        Pleased = 2,
    }

    private sealed partial class TutorialBuddyPortraitCard : Control
    {
        private const double TickRate = 60.0;
        private const double MouthFrameSeconds = 0.085;
        private const ulong BlinkSeed = 0x5455544F5249414CUL; // "TUTORIAL"

        private readonly SandboxRoot _sandbox;
        private readonly BlinkModel _blink;
        private SubViewportContainer _portraitContainer = null!;
        private BuddyPreviewSurface _preview = null!;
        private TutorialPortraitMood _mood = TutorialPortraitMood.Friendly;
        private bool _speaking;
        private double _tickRemainder;
        private double _speechElapsed;
        private FaceRenderState? _lastFace;
        private bool _ready;

        public TutorialBuddyPortraitCard(SandboxRoot sandbox)
        {
            _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
            BlinkParameters parameters = GodotObject.IsInstanceValid(_sandbox.Face) &&
                                         GodotObject.IsInstanceValid(_sandbox.Face.Profile)
                ? _sandbox.Face.Profile.ToData().ToBlinkParameters()
                : new BlinkParameters(240, 720, 14);
            _blink = new BlinkModel(new SeededRandomSource(BlinkSeed), parameters);
            ProcessMode = ProcessModeEnum.Always;
        }

        public override void _Ready()
        {
            Win98ThemeFactory.RepaintOnPaletteChange(this);
            BuildPortrait();
            Resized += LayoutPortrait;
            LayoutPortrait();
            _ready = true;
            SyncLiveAppearance();
            RefreshFace();
        }

        public override void _ExitTree()
        {
            Resized -= LayoutPortrait;
        }

        public override void _Process(double delta)
        {
            if (!_ready || !Visible || !IsVisibleInTree())
                return;

            double safeDelta = Math.Max(0.0, delta);
            _tickRemainder += safeDelta * TickRate;
            int ticks = (int)_tickRemainder;
            _tickRemainder -= ticks;
            if (_speaking)
                _speechElapsed += safeDelta;

            FaceFeaturePose pose = PoseFor(_mood);
            bool oldBlink = _blink.EyesClosed;
            if (ticks > 0)
                _blink.Update(!pose.EyesBlinkable, ticks);

            bool mouthBoundary = _speaking &&
                (int)((_speechElapsed - safeDelta) / MouthFrameSeconds) !=
                (int)(_speechElapsed / MouthFrameSeconds);
            if (oldBlink != _blink.EyesClosed || mouthBoundary)
                RefreshFace();
        }

        public void SetStep(string stepId)
        {
            TutorialPortraitMood mood = MoodFor(stepId);
            bool changed = mood != _mood;
            _mood = mood;
            if (_ready)
            {
                SyncLiveAppearance();
                if (changed)
                    RefreshFace();
            }
            QueueRedraw();
        }

        public void SetSpeaking(bool speaking)
        {
            if (_speaking == speaking)
                return;
            _speaking = speaking;
            _speechElapsed = 0.0;
            if (_ready)
                RefreshFace();
        }

        public override void _Draw()
        {
            Rect2 rect = new(Vector2.Zero, Size);
            DrawRect(rect, Win98ThemeFactory.Face, true);
            DrawLine(Vector2.Zero, new Vector2(Size.X, 0), Win98ThemeFactory.Light, 2);
            DrawLine(Vector2.Zero, new Vector2(0, Size.Y), Win98ThemeFactory.Light, 2);
            DrawLine(new Vector2(0, Size.Y - 1), new Vector2(Size.X, Size.Y - 1), Win98ThemeFactory.Dark, 2);
            DrawLine(new Vector2(Size.X - 1, 0), new Vector2(Size.X - 1, Size.Y), Win98ThemeFactory.Dark, 2);
            DrawRect(new Rect2(3, 3, Math.Max(0, Size.X - 6), 20), Win98ThemeFactory.ActiveTitle, true);
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(8, 18),
                "Guide",
                HorizontalAlignment.Left,
                -1,
                12,
                Win98ThemeFactory.TitleText);
        }

        private void BuildPortrait()
        {
            _portraitContainer = new SubViewportContainer
            {
                Name = "TutorialBuddyPortrait",
                Stretch = true,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(_portraitContainer);

            _preview = new BuddyPreviewSurface { Name = "TutorialBuddyPortraitViewport" };
            _preview.Configure(
                rigName: "TutorialBuddyPortraitRig",
                viewportSize: new Vector2I(256, 320),
                transparentBackground: true,
                rigProfile: _sandbox.Buddy.Rig.Profile,
                visualProfile: _sandbox.Buddy.VisualProfile,
                cameraSize: 118.0f,
                cameraPosition: new Vector3(0, -24, 600),
                lightRotationDegrees: new Vector3(-30, -20, 0),
                lightEnergy: 0.9f,
                sourceOrigin: Vector2.Zero,
                face: ":)",
                visibilityOwner: _portraitContainer);
            _portraitContainer.AddChild(_preview);
            _preview.Rig.ApplyRestPose();

            Vector2 head = _preview.Source.ReadTransform(BuddyPartId.Head).Position;
            Vector2 torso = _preview.Source.ReadTransform(BuddyPartId.Torso).Position;
            Vector2 center = head.Lerp(torso, 0.32f);
            _preview.SetCameraFrame(new Vector3(center.X, center.Y, 600), 118.0f);
        }

        private void LayoutPortrait()
        {
            if (!GodotObject.IsInstanceValid(_portraitContainer))
                return;
            _portraitContainer.Position = new Vector2(7, 27);
            _portraitContainer.Size = new Vector2(
                Math.Max(1.0f, Size.X - 14.0f),
                Math.Max(1.0f, Size.Y - 34.0f));
        }

        private void SyncLiveAppearance()
        {
            if (!GodotObject.IsInstanceValid(_preview) ||
                !GodotObject.IsInstanceValid(_sandbox.VisualPresenter?.RigView))
            {
                return;
            }
            _preview.CopyPresentationFrom(_sandbox.VisualPresenter.RigView);
            _preview.Rig.ApplyRestPose();
        }

        private void RefreshFace()
        {
            if (!GodotObject.IsInstanceValid(_preview))
                return;

            FaceFeaturePose pose = PoseFor(_mood);
            FaceRenderState state = FaceComposer.Compose(
                pose,
                _blink.EyesClosed,
                chewActive: false,
                chewFrame: 0,
                faceSuppressed: false,
                pupilX: 0.0f,
                pupilY: 0.0f);

            if (_speaking && !_blink.EyesClosed)
            {
                bool open = ((int)(_speechElapsed / MouthFrameSeconds) & 1) == 0;
                state = state with
                {
                    Mouth = open
                        ? (_mood == TutorialPortraitMood.Neutral
                            ? FaceMouthPose.SmallO
                            : FaceMouthPose.OpenSmile)
                        : pose.Mouth,
                };
            }

            if (_lastFace == state)
                return;
            _lastFace = state;
            _preview.Rig.SetPreviewFaceState(state);
            _preview.RequestSingleFrame();
        }

        private static FaceFeaturePose PoseFor(TutorialPortraitMood mood) =>
            FaceExpressionCatalog.Resolve(mood switch
            {
                TutorialPortraitMood.Neutral => ":|",
                TutorialPortraitMood.Pleased => "^_^",
                _ => ":)",
            });

        private static TutorialPortraitMood MoodFor(string stepId) => stepId switch
        {
            TutorialStepIds.AdmirePaintedBuddy or
            TutorialStepIds.SaveAndExitPaintBackground or
            TutorialStepIds.AdmireStudioBuddy or
            TutorialStepIds.Farewell => TutorialPortraitMood.Pleased,

            TutorialStepIds.ChargedBatHit or
            TutorialStepIds.UnequipTool or
            TutorialStepIds.PaintBackground or
            TutorialStepIds.ResizeWorkCompanion => TutorialPortraitMood.Neutral,

            _ => TutorialPortraitMood.Friendly,
        };
    }
}
