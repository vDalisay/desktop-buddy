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
/// Presentation-only live Buddy guide for the first-session tutorial. Main and Work helper hosts
/// use the same physics-free portrait implementation. Each portrait owns one stable authored guide
/// appearance plus independent blink/look/mouth state and never shares gameplay bodies, reactions,
/// autonomy or clocks with the live Buddy.
/// </summary>
public sealed partial class LiveTutorialBuddyPresenter : ITutorialCharacterPresenter
{

    private readonly FirstSessionGuidanceController _owner;
    private readonly SandboxRoot _sandbox;
    private TutorialBuddyPortraitCard? _card;
    private TutorialBuddyPortraitCard? _workCard;

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

        // Historical node name retained so the long-standing tutorial closure oracle keeps
        // locating the guide. The node is now the live 3D portrait, not optional PNG art.
        TutorialBuddyPortraitCard card = EnsureCard(
            ref _card,
            _owner.GuideSlot,
            "DemoTutorialBuddy");
        card.SetStep(stepId);
        card.Visible = true;
        if (GodotObject.IsInstanceValid(_workCard))
            _workCard!.Visible = false;
    }

    public void PresentWork(Control host, string stepId)
    {
        if (!GodotObject.IsInstanceValid(host))
            return;
        TutorialBuddyPortraitCard card = EnsureCard(
            ref _workCard,
            host,
            "LiveTutorialBuddyWork");
        card.SetStep(stepId);
        card.Visible = true;
        if (GodotObject.IsInstanceValid(_card))
            _card!.Visible = false;
    }

    public void DismissWork()
    {
        if (GodotObject.IsInstanceValid(_workCard))
        {
            _workCard!.SetSpeaking(false);
            _workCard.Visible = false;
        }
    }

    public void Dismiss()
    {
        if (GodotObject.IsInstanceValid(_card))
        {
            _card!.SetSpeaking(false);
            _card.Visible = false;
        }
        DismissWork();
    }

    public void SetSpeaking(bool speaking)
    {
        if (GodotObject.IsInstanceValid(_card) && _card!.IsVisibleInTree())
            _card.SetSpeaking(speaking);
        if (GodotObject.IsInstanceValid(_workCard) && _workCard!.IsVisibleInTree())
            _workCard.SetSpeaking(speaking);
    }

    /// <summary>
    /// Converts a desktop pointer position into the normalized look vector consumed by the
    /// presentation-only face/head pose. The elliptical range makes horizontal tracking a little
    /// broader than vertical tracking, then clamps diagonals to one unit so pupils never pin into
    /// an eye corner. This stays pure so the headless expressive scenario can lock the contract.
    /// </summary>
    internal static Vector2 ResolvePointerLook(
        Vector2 pointerScreenPosition,
        Vector2 portraitScreenCenter,
        Vector2 halfExtent)
    {
        if (!pointerScreenPosition.IsFinite() || !portraitScreenCenter.IsFinite() ||
            !halfExtent.IsFinite() || halfExtent.X <= 0.0f || halfExtent.Y <= 0.0f)
        {
            return Vector2.Zero;
        }

        Vector2 delta = pointerScreenPosition - portraitScreenCenter;
        var look = new Vector2(delta.X / halfExtent.X, delta.Y / halfExtent.Y);
        if (look.LengthSquared() > 1.0f)
            look = look.Normalized();
        return look;
    }

    private TutorialBuddyPortraitCard EnsureCard(
        ref TutorialBuddyPortraitCard? card,
        Control host,
        string name)
    {
        if (GodotObject.IsInstanceValid(card))
        {
            if (card!.GetParent() != host)
            {
                card.GetParent()?.RemoveChild(card);
                host.AddChild(card);
            }
            return card;
        }

        card = new TutorialBuddyPortraitCard(_sandbox)
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(card);
        return card;
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
        /// <summary>Portrait crop: head centred with the shoulder caps just in frame.</summary>
        private const float PortraitHeadFocusBias = 0.10f;
        private const float PortraitHeadFillFactor = 2.9f;
        private const ulong LookSeed = 0x5455544F4C4F4F4BUL; // "TUTOLOOK"
        private const ulong BlinkSeed = 0x5455544F5249414CUL; // "TUTORIAL"

        private readonly SandboxRoot _sandbox;
        private readonly BlinkModel _blink;
        private LookAtModel? _look;
        private LookAtParameters _lookParameters;
        private float _lookYawRadians;
        private float _lookPitchRadians;
        private SubViewportContainer _portraitContainer = null!;
        private BuddyPreviewSurface _preview = null!;
        private TutorialPortraitMood _mood = TutorialPortraitMood.Friendly;
        private bool _speaking;
        private double _tickRemainder;
        private double _speechElapsed;
        private double _idleSeconds;
        private Vector2 _pointerLook;
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
            _pointerLook = CurrentPointerLook();
            RefreshFace();
            ApplyIdlePose();
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

            // Raw aim in, model-owned easing out: smoothing the input as well would damp the
            // gaze twice and stop it matching the live Buddy's response.
            _pointerLook = CurrentPointerLook();
            Vector2 previousPupil = LookPupilOffset();
            UpdateLook(safeDelta);
            bool lookChanged = previousPupil != LookPupilOffset();

            if (oldBlink != _blink.EyesClosed || mouthBoundary || lookChanged)
                RefreshFace();

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
                // Superseded by the measured shoulders-up frame below, before the first render.
                cameraPosition: new Vector3(0, 0, 600),
                lightRotationDegrees: new Vector3(-30, -20, 0),
                lightEnergy: 0.9f,
                sourceOrigin: Vector2.Zero,
                face: ":)",
                visibilityOwner: _portraitContainer);
            _portraitContainer.AddChild(_preview);

            // Stable authored guide, not a mirror of whichever player Buddy is active.
            _preview.Rig.ApplyAppearance(BuiltInCharacterAppearance.Value);
            _preview.Rig.ApplyRestPose();

            // Rig space is 2D pixels with Y down; the preview world is Y up. Copying the 2D Y
            // straight into the camera mirrors the focus point through the torso and frames the
            // legs instead of the head — every other boundary crossing in this file already goes
            // through WorldPlaneMapping, and this one has to as well.
            Vector2 head = _preview.Source.ReadTransform(BuddyPartId.Head).Position;
            Vector2 torso = _preview.Source.ReadTransform(BuddyPartId.Torso).Position;
            Vector3 focus = WorldPlaneMapping.To3D(head.Lerp(torso, PortraitHeadFocusBias));
            // Framed off the head's own radius rather than a magic number, so a rig change moves
            // the crop with it: the head fills the plate and the shoulder caps just enter frame.
            float headRadius = _sandbox.Buddy.Rig.Profile.FindPart(BuddyPartId.Head)?.Radius ?? 24.0f;
            _preview.SetCameraFrame(
                new Vector3(focus.X, focus.Y, 600),
                headRadius * PortraitHeadFillFactor);
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

        private Vector2 CurrentPointerLook()
        {
            Window window = GetWindow();
            Vector2 windowOrigin = GodotObject.IsInstanceValid(window)
                ? (Vector2)window.Position
                : Vector2.Zero;
            Vector2 portraitCenter = windowOrigin + GetGlobalRect().GetCenter();
            // The guide watches the whole desktop, so the sweep is normalized against the screen
            // rather than a fixed pixel window: the cursor reaches the edge of the cone exactly
            // when it reaches the edge of the display.
            Vector2 screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
            Vector2 halfExtent = new(
                Math.Max(1.0f, screen.X * 0.5f),
                Math.Max(1.0f, screen.Y * 0.5f));
            return ResolvePointerLook(
                (Vector2)DisplayServer.MouseGetPosition(), portraitCenter, halfExtent);
        }

        /// <summary>
        /// The guide watches the cursor with the same model the live Buddy uses when a cursor tool
        /// is engaged — same cone, same easing, same quantized pupils — so the portrait's gaze
        /// reads as the Buddy looking at you rather than a separate bespoke wobble. Only the
        /// engagement rule differs: gameplay engages inside a small radius around the head, while
        /// the guide is always engaged and sweeps its cone across the whole display.
        ///
        /// <para>The pure model is reused, never the gameplay component: the portrait must own no
        /// gameplay authority, and the component samples damage, care, activities and reactions.</para>
        /// </summary>
        private void UpdateLook(double deltaSeconds)
        {
            if (_look is null)
            {
                // The live Buddy's own tuning is the only source: a local copy of these numbers
                // would drift from gameplay, which is precisely what "the same look" rules out.
                if (!GodotObject.IsInstanceValid(_sandbox.HeadLookAt) ||
                    !GodotObject.IsInstanceValid(_sandbox.HeadLookAt.Profile))
                {
                    _lookYawRadians = 0.0f;
                    _lookPitchRadians = 0.0f;
                    return;
                }

                _lookParameters = _sandbox.HeadLookAt.Profile.ToData().ToLookAtParameters();
                _look = new LookAtModel(new SeededRandomSource(LookSeed), _lookParameters);
            }

            // Map the normalized screen sweep onto the cone: at the edge of the display the
            // aim lands exactly on the cone limit, because Aim() is atan2(delta, gazeDepth).
            float reachX = MathF.Tan(Mathf.DegToRad(_lookParameters.ConeYawDegrees)) *
                _lookParameters.GazeDepth;
            float reachY = MathF.Tan(Mathf.DegToRad(_lookParameters.ConePitchDegrees)) *
                _lookParameters.GazeDepth;
            Vector2 headPosition = _preview.Source.ReadTransform(BuddyPartId.Head).Position;
            Vector2 cursor = headPosition + new Vector2(_pointerLook.X * reachX, _pointerLook.Y * reachY);

            LookAtAngles angles = _look.Update(
                new LookAtInputs(
                    InteractionEngaged: true,
                    CursorX: cursor.X,
                    CursorY: cursor.Y,
                    ItemTargetValid: false,
                    ItemX: 0.0f,
                    ItemY: 0.0f,
                    TicksSinceImpact: int.MaxValue,
                    ImpactX: 0.0f,
                    ImpactY: 0.0f,
                    AmbientAllowed: false,
                    FaceSuppressed: false,
                    HeadX: headPosition.X,
                    HeadY: headPosition.Y),
                ticksElapsed: 1,
                deltaSeconds);

            bool animate = Win98MotionPolicy.Allows(_sandbox.Shell.CurrentLocalSettings);
            _lookYawRadians = animate ? Mathf.DegToRad(angles.YawDegrees) : 0.0f;
            _lookPitchRadians = animate ? Mathf.DegToRad(angles.PitchDegrees) : 0.0f;
        }

        private Vector2 LookPupilOffset() => _look is null
            ? Vector2.Zero
            : new Vector2(_look.PupilOffsetX, _look.PupilOffsetY);

        private void ApplyIdlePose()
        {
            if (!GodotObject.IsInstanceValid(_preview))
                return;

            bool animate = Win98MotionPolicy.Allows(_sandbox.Shell.CurrentLocalSettings);
            float bobX = animate ? Mathf.Sin((float)(_idleSeconds * 1.17)) * 0.9f : 0.0f;
            float bobY = animate ? Mathf.Sin((float)(_idleSeconds * 1.63 + 0.7)) * 0.7f : 0.0f;
            // Only the gentle idle sway stays as a roll. The cursor no longer rolls or slides the
            // head: it turns it, through the look model, exactly as BuddyVisualPresenter does.
            float headTilt = animate ? Mathf.Sin((float)(_idleSeconds * 0.83 + 0.3)) * 0.018f : 0.0f;

            BuddyVisualPartPose Part(
                BuddyPartId id,
                Vector2 offset,
                float rotation = 0.0f,
                bool isHead = false)
            {
                BuddyVisualTransform source = _preview.Source.ReadTransform(id);
                Vector2 position = source.Position + offset;
                var rendered = new BuddyVisualTransform(position, rotation, Vector2.Zero);
                // Head sockets carry (pitch, yaw, roll) — the same component order the live
                // presenter composes, so the guide's gaze and the Buddy's read identically.
                Vector3 globalRotation = isHead
                    ? new Vector3(
                        _lookPitchRadians,
                        _lookYawRadians,
                        WorldPlaneMapping.To3DRotationZ(rotation))
                    : new Vector3(0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(rotation));
                return new BuddyVisualPartPose(rendered, WorldPlaneMapping.To3D(position), globalRotation);
            }

            Vector2 bodyBob = new(bobX * 0.35f, bobY * 0.35f);
            Vector2 headBob = new(bobX, bobY);

            FaceRenderState face = _lastFace ?? FaceComposer.Compose(
                PoseFor(_mood),
                _blink.EyesClosed,
                chewActive: false,
                chewFrame: 0,
                faceSuppressed: false,
                pupilX: LookPupilOffset().X,
                pupilY: LookPupilOffset().Y);

            _preview.Rig.ApplyPose(new BuddyVisualPoseFrame(
                Part(BuddyPartId.Head, headBob, headTilt, isHead: true),
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
            FaceRenderState state = FaceComposer.Compose(
                pose,
                _blink.EyesClosed,
                chewActive: false,
                chewFrame: 0,
                faceSuppressed: false,
                pupilX: LookPupilOffset().X,
                pupilY: LookPupilOffset().Y);

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

        private static FaceFeaturePose PoseFor(TutorialPortraitMood mood) => mood switch
        {
            TutorialPortraitMood.Neutral => FaceExpressionCatalog.Resolve(":|"),
            // Keep the pleased read while retaining pupils so the guide can continue following
            // the pointer. The old ^_^ pose deliberately has no pupils.
            TutorialPortraitMood.Pleased => new FaceFeaturePose(
                FaceEyePose.Open,
                FaceBrowPose.Raised,
                FaceMouthPose.OpenSmile),
            _ => FaceExpressionCatalog.Resolve(":)"),
        };

        private static TutorialPortraitMood MoodFor(string stepId) => stepId switch
        {
            TutorialStepIds.AdmirePaintedBuddy or
            TutorialStepIds.SaveAndExitPaintBackground or
            TutorialStepIds.AdmireStudioBuddy => TutorialPortraitMood.Pleased,

            // The goodbye keeps the guide's ordinary closed smile. Pleased is an open-mouth
            // "oh!" that reads as surprise on a line that is just saying farewell, and the last
            // thing the player sees should be the face the guide wore all the way through.
            TutorialStepIds.Farewell => TutorialPortraitMood.Friendly,

            TutorialStepIds.ChargedBatHit or
            TutorialStepIds.UnequipTool or
            TutorialStepIds.PaintBackground or
            TutorialStepIds.ResizeWorkCompanion => TutorialPortraitMood.Neutral,

            _ => TutorialPortraitMood.Friendly,
        };
    }
}
