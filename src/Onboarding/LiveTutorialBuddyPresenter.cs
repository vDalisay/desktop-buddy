using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Presentation-only live Buddy guide for the first-session tutorial. It uses the same
/// physics-free <see cref="BuddyPreviewSurface"/> as Work, Character Editor and Workshop capture,
/// owns one stable authored guide appearance plus its own blink/look/mouth state, and never shares
/// gameplay bodies, reactions, autonomy or clocks with the live Buddy.
/// </summary>
public sealed partial class LiveTutorialBuddyPresenter : ITutorialCharacterPresenter
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
        private const double LookRefreshSeconds = 0.22;
        private const ulong BlinkSeed = 0x5455544F5249414CUL; // "TUTORIAL"

        private readonly SandboxRoot _sandbox;
        private readonly BlinkModel _blink;
        private SubViewportContainer _portraitContainer = null!;
        private BuddyPreviewSurface _preview = null!;
        private TutorialPortraitMood _mood = TutorialPortraitMood.Friendly;
        private bool _speaking;
        private double _tickRemainder;
        private double _speechElapsed;
        private double _idleSeconds;
        private double _lookRefreshRemaining;
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
            RefreshFace();
            ApplyIdlePose();
        }

        public override void _ExitTree()
        {
            Resized -= LayoutPortrait;
        }

        public override void _Process(double delta)
        {
            // The card and its SubViewport both stop doing work when the tutorial guide is hidden.
            if (!_ready || !Visible || !IsVisibleInTree())
                return;

            double safeDelta = Math.Max(0.0, delta);
            _idleSeconds += safeDelta;
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

            _lookRefreshRemaining -= safeDelta;
            bool lookBoundary = _lookRefreshRemaining <= 0.0;
            if (lookBoundary)
                _lookRefreshRemaining += LookRefreshSeconds;

            if (oldBlink != _blink.EyesClosed || mouthBoundary || lookBoundary)
                RefreshFace();

            // Small deterministic head/bob motion makes the portrait feel alive without any
            // gameplay RNG or autonomy. The rest anatomy remains the trusted static preview pose.
            ApplyIdlePose();
        }

        public void SetStep(string stepId)
        {
            TutorialPortraitMood mood = MoodFor(stepId);
            bool changed = mood != _mood;
            _mood = mood;
            if (_ready && changed)
            {
                RefreshFace();
                ApplyIdlePose();
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
            {
                RefreshFace();
                ApplyIdlePose();
            }
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

            // The guide is intentionally a stable authored character, not a mirror of whichever
            // player Buddy happens to be active. BuiltInCharacterAppearance is the trusted
            // data-driven baseline and can later be swapped for another authored appearance here.
            _preview.Rig.ApplyAppearance(BuiltInCharacterAppearance.Value);
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

        private void ApplyIdlePose()
        {
            if (!GodotObject.IsInstanceValid(_preview))
                return;

            float bobX = Mathf.Sin((float)(_idleSeconds * 1.17)) * 0.9f;
            float bobY = Mathf.Sin((float)(_idleSeconds * 1.63 + 0.7)) * 0.7f;
            float headTilt = Mathf.Sin((float)(_idleSeconds * 0.83 + 0.3)) * 0.035f;

            BuddyVisualPartPose Part(BuddyPartId id, Vector2 offset, float rotation = 0.0f)
            {
                BuddyVisualTransform source = _preview.Source.ReadTransform(id);
                Vector2 position = source.Position + offset;
                var rendered = new BuddyVisualTransform(position, rotation, Vector2.Zero);
                return new BuddyVisualPartPose(
                    rendered,
                    WorldPlaneMapping.To3D(position),
                    new Vector3(0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(rotation)));
            }

            Vector2 bodyBob = new(bobX * 0.35f, bobY * 0.35f);
            Vector2 headBob = new(bobX, bobY);
            FaceRenderState face = _lastFace ?? FaceComposer.Compose(
                PoseFor(_mood),
                _blink.EyesClosed,
                chewActive: false,
                chewFrame: 0,
                faceSuppressed: false,
                pupilX: 0.0f,
                pupilY: 0.0f);

            _preview.Rig.ApplyPose(new BuddyVisualPoseFrame(
                Part(BuddyPartId.Head, headBob, headTilt),
                Part(BuddyPartId.Torso, bodyBob),
                Part(BuddyPartId.LeftHand, bodyBob),
                Part(BuddyPartId.RightHand, bodyBob),
                Part(BuddyPartId.LeftFoot, bodyBob),
                Part(BuddyPartId.RightFoot, bodyBob),
                bodyYawRadians: 0.0f,
                faceState: face,
                fallbackFace: ":)",
                fallbackFaceRotation: 0.0f));
        }

        private void RefreshFace()
        {
            if (!GodotObject.IsInstanceValid(_preview))
                return;

            FaceFeaturePose pose = PoseFor(_mood);
            float pupilX = Mathf.Sin((float)(_idleSeconds * 0.71 + 0.4)) * 0.22f;
            float pupilY = Mathf.Sin((float)(_idleSeconds * 0.53 + 1.1)) * 0.10f;
            FaceRenderState state = FaceComposer.Compose(
                pose,
                _blink.EyesClosed,
                chewActive: false,
                chewFrame: 0,
                faceSuppressed: false,
                pupilX: pupilX,
                pupilY: pupilY);

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
