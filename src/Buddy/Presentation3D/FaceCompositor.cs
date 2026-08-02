using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Runtime semantic face controller. It owns reaction priority, routed-tick blink/chew
/// clocks, and pupil sampling only. Pixel drawing is delegated through
/// <see cref="BuddyVisualPoseFrame"/> to the rig view's parameterized compositor.
/// </summary>
[GlobalClass]
public partial class FaceCompositor : Node
{
    private const ulong BlinkStreamSalt = 0xB11B_FACE_2026_0720UL;

    public const int TextureSize = ParametricFaceCompositor.TextureSize;
    public const float PlateWorldSize = ParametricFaceCompositor.PlateWorldSize;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public BuddyReactionComponent Reactions { get; set; } = null!;
    [Export] public ActivityAnimator Activities { get; set; } = null!;
    [Export] public BuddyExpressionProfile Profile { get; set; } = null!;
    [Export] public BuddyVisualProfile VisualProfile { get; set; } = null!;
    [Export] public HeadLookAtComponent? HeadLookAt { get; set; }

    private BlinkModel _blink = null!;
    private long _lastRoutedTick;
    private bool _hasComposed;

    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Compatibility surface for the accepted default. Persisted character IDs, not
    /// shop-reserved FaceStyleId values, select the actual renderer registry.
    /// </summary>
    public FaceStyleId Style { get; private set; } = FaceStyleId.SoftOval;

    public FaceRenderState LastComposedState { get; private set; }

    /// <summary>
    /// Semantic-key changes observed by this controller. The pixel render oracle lives on
    /// BuddyVisualRigView.CharacterFaceRenderCount and follows the full appearance key.
    /// </summary>
    public int RenderCount { get; private set; }

    /// <summary>GPU output moved to BuddyVisualRigView in A4.</summary>
    public Texture2D? OutputTexture => null;

    public void Initialize()
    {
        if (IsInitialized)
            return;

        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Reactions) ||
            !GodotObject.IsInstanceValid(Activities) || !Activities.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) ||
            !GodotObject.IsInstanceValid(VisualProfile) ||
            !GodotObject.IsInstanceValid(VisualProfile.Look))
        {
            throw new InvalidOperationException("FaceCompositor dependencies are incomplete.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy expression profile: {string.Join("; ", errors)}");
        }

        foreach (string face in FaceExpressionMap.Faces)
        {
            if (!FaceExpressionMap.TryResolve(face, out _))
                throw new InvalidOperationException($"Face expression map misses '{face}'.");
        }

        Reseed(Buddy.AutonomousMotion.Seed);
        Buddy.AutonomyReseeded += Reseed;
        Reactions.FaceChanged += OnFaceChanged;
        _lastRoutedTick = Buddy.RoutedTicks;
        IsInitialized = true;
        Evaluate();
    }

    public override void _Process(double delta)
    {
        if (IsInitialized)
            Evaluate();
    }

    public override void _ExitTree()
    {
        if (!IsInitialized)
            return;

        if (GodotObject.IsInstanceValid(Buddy))
            Buddy.AutonomyReseeded -= Reseed;
        if (GodotObject.IsInstanceValid(Reactions))
            Reactions.FaceChanged -= OnFaceChanged;
    }

    public void Reseed(ulong seed) => _blink = new BlinkModel(
        new SeededRandomSource(seed ^ BlinkStreamSalt),
        Profile.ToData().ToBlinkParameters());

    /// <summary>
    /// Produces one composed semantic state. No viewport, material, character document, or
    /// renderer is touched here.
    /// </summary>
    public FaceRenderState Evaluate()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("FaceCompositor used before initialization.");

        long now = Buddy.RoutedTicks;
        int ticksElapsed = (int)Math.Clamp(now - _lastRoutedTick, 0, int.MaxValue);
        _lastRoutedTick = now;

        string face = Reactions.CurrentFace;
        FaceFeaturePose pose = FaceExpressionMap.Resolve(face);
        if (HeadLookAt is { IsInitialized: true, CurrentSource: LookAtSource.Item } &&
            !Profile.SuppressesLookAt(face) &&
            (pose.HasPupils || pose.Eyes == FaceEyePose.HappyArc))
        {
            pose = pose with { Eyes = FaceEyePose.Wide };
        }

        _blink.Update(!pose.EyesBlinkable, ticksElapsed);
        bool chewActive = Activities.Current == ActivityId.Eat;
        int chewFrame = ChewCycle.FrameAt(now, Profile.ChewCycleTicks);
        Vector2 pupils = HeadLookAt is { IsInitialized: true }
            ? HeadLookAt.PupilOffset
            : Vector2.Zero;

        FaceRenderState state = FaceComposer.Compose(
            pose,
            _blink.EyesClosed,
            chewActive,
            chewFrame,
            Profile.SuppressesLookAt(face),
            pupils.X,
            pupils.Y);

        if (!_hasComposed || state != LastComposedState)
        {
            _hasComposed = true;
            LastComposedState = state;
            RenderCount++;
        }

        return state;
    }

    private void OnFaceChanged(string face)
    {
        if (IsInitialized)
            Evaluate();
    }
}
