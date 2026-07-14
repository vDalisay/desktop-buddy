using System;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// One raw solver contact observed on a part during the last completed physics
/// step, before deduplication (RAGDOLL §7.1). The impact pipeline consumes these
/// on the following fixed tick — accepted pain trails physical contact by one
/// 120 Hz tick by design (ARCHITECTURE §23).
/// </summary>
public readonly record struct RawPartContact(
    GodotObject? Collider,
    float Impulse,
    float RelativeSpeed,
    Vector2 Point,
    Vector2 Normal);

/// <summary>
/// One authoritative circular rigid body. It owns only body configuration and
/// direct shape rendering; behavior and structural forces live in components.
/// </summary>
[GlobalClass]
public partial class PuppetPartBody : RigidBody2D
{
    private const int CircleSegments = 40;
    private const float OutlineWidth = 2.0f;
    // Must match MaxContactsReported: both contact signals and direct-state
    // queries only see up to that many contacts (ARCHITECTURE §23).
    private const int ContactBufferSize = 8;
    private const float QuarterTurn = Mathf.Pi * 0.5f;
    private static readonly Color OutlineColor = new("183042");

    private readonly RawPartContact[] _pendingContacts = new RawPartContact[ContactBufferSize];
    private string _face = string.Empty;
    private bool _faceUsesSidewaysAsciiLayout;

    [Export] public BuddyPartId PartId { get; set; }
    [Export] public CollisionShape2D Collider { get; set; } = null!;

    public float Radius { get; private set; } = 16.0f;
    public Color FillColor { get; private set; } = new("7ac7ff");
    public bool HasSupportContact { get; private set; }
    public int SupportContactCount { get; private set; }
    public int PendingContactCount { get; private set; }
    public string Face => _face;
    public float FaceDrawRotation => PartId == BuddyPartId.Head
        ? (_faceUsesSidewaysAsciiLayout ? QuarterTurn : 0.0f) - GlobalRotation
        : 0.0f;

    public RawPartContact GetPendingContact(int index) => _pendingContacts[index];

    /// <summary>
    /// Marks the buffered contacts consumed. The next completed physics step
    /// refills the buffer; a frozen/paused body simply leaves it empty, so the
    /// pipeline can never read the same step's contacts twice.
    /// </summary>
    public void ClearPendingContacts() => PendingContactCount = 0;

    /// <summary>Presentation-only face text; gameplay state is owned elsewhere.</summary>
    public void SetFace(string face)
    {
        if (_face == face) return;
        _face = face;
        _faceUsesSidewaysAsciiLayout = UsesSidewaysAsciiLayout(face);
        QueueRedraw();
    }

    public static bool UsesSidewaysAsciiLayout(string face) => face is
        ":)" or ":(" or ":/" or ":|" or ":3" or ">:(";

    public void Configure(PuppetPartDefinition definition, Vector2 globalOrigin)
    {
        AddToGroup("buddy_parts");
        if (definition.PartId != PartId)
        {
            throw new InvalidOperationException($"Body {PartId} received definition {definition.PartId}.");
        }

        if (!GodotObject.IsInstanceValid(Collider) || Collider.Shape is not CircleShape2D circle)
        {
            throw new InvalidOperationException($"Body {PartId} requires an injected CircleShape2D collider.");
        }

        Radius = definition.Radius;
        FillColor = definition.FillColor;
        Mass = definition.Mass;
        LinearDamp = definition.LinearDamp;
        AngularDamp = definition.AngularDamp;
        GlobalPosition = globalOrigin + definition.RestPosition;
        GlobalRotation = 0.0f;
        LinearVelocity = Vector2.Zero;
        AngularVelocity = 0.0f;

        circle.Radius = Radius;
        CollisionLayer = CollisionLayers.BuddyParts;
        CollisionMask = CollisionLayers.MaskBuddyParts;
        CanSleep = false;
        ContactMonitor = true;
        MaxContactsReported = ContactBufferSize;
        QueueRedraw();
    }

    public bool HasFiniteState() =>
        GlobalPosition.IsFinite() &&
        LinearVelocity.IsFinite() &&
        float.IsFinite(GlobalRotation) &&
        float.IsFinite(AngularVelocity);

    public override void _IntegrateForces(PhysicsDirectBodyState2D state)
    {
        HasSupportContact = false;
        SupportContactCount = 0;
        PendingContactCount = 0;
        int contactCount = state.GetContactCount();
        for (int index = 0; index < contactCount; index++)
        {
            GodotObject? colliderObject = state.GetContactColliderObject(index);

            // Buffer every solver contact for the impact pipeline. Buddy parts
            // never collide with each other (CollisionLayers), so each entry is
            // an external source: room bounds, loose object, or physical tool.
            if (PendingContactCount < ContactBufferSize)
            {
                float impulse = state.GetContactImpulse(index).Length();
                float relativeSpeed =
                    (state.GetContactColliderVelocityAtPosition(index) -
                     state.GetContactLocalVelocityAtPosition(index)).Length();
                // Despite the legacy "local" method names, Godot 4.6 reports
                // both values in the global physics coordinate system. Applying
                // this body's transform again displaced impact VFX far away from
                // the actual solver contact and rotated its presentation rays.
                Vector2 point = state.GetContactLocalPosition(index);
                Vector2 normal = state.GetContactLocalNormal(index).Normalized();
                _pendingContacts[PendingContactCount] =
                    new RawPartContact(colliderObject, impulse, relativeSpeed, point, normal);
                PendingContactCount++;
            }

            if (colliderObject is not CollisionObject2D collider ||
                (collider.CollisionLayer & CollisionLayers.RoomBounds) == 0)
            {
                continue;
            }

            // GetContactLocalNormal returns the normal in WORLD space in Godot 4
            // (the "local" is legacy naming), verified on 4.6.1. Rotating it by the
            // body's own rotation was a bug: a circular foot spins freely at rest,
            // and once its rotation entered the ~63-117 degree band the floor normal
            // was rotated out of the support cone, dropping HasSupportContact to false
            // mid-idle. That starved the standing detector, ran the recovery clock to
            // its 12 s timeout, and hard-reset-teleported the buddy on every soak seed.
            Vector2 worldNormal = state.GetContactLocalNormal(index);
            if (Mathf.Abs(worldNormal.Y) > 0.45f)
            {
                HasSupportContact = true;
                SupportContactCount++;
            }
        }
    }

    public override void _Draw()
    {
        DrawCircle(Vector2.Zero, Radius, FillColor, true, -1.0f, true);
        DrawArc(Vector2.Zero, Radius, 0.0f, Mathf.Tau, CircleSegments, OutlineColor, OutlineWidth, true);
        if (PartId == BuddyPartId.Head && !string.IsNullOrEmpty(_face))
        {
            // Colon-style emoticons are authored sideways and need a quarter
            // turn; already-front-facing glyphs such as x_x and o_o do not. Both
            // layouts still counter-rotate the freely spinning physical head.
            DrawSetTransform(Vector2.Zero, FaceDrawRotation, Vector2.One);
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(-Radius, 6.0f),
                _face,
                HorizontalAlignment.Center,
                Radius * 2.0f,
                14,
                OutlineColor);
        }
    }

    public override void _Process(double delta)
    {
        // Draw commands cache their local counter-rotation, so refresh only the
        // one head circle while it physically rotates.
        if (PartId == BuddyPartId.Head && !string.IsNullOrEmpty(_face))
            QueueRedraw();
    }
}
