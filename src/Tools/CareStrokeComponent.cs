using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Converts held pointer strokes into semantic Pet distance or Tickle contact.
/// It owns contact geometry and the independent seeded favorite-spot stream;
/// the Domain care model owns thresholds, anger, cooldown, and hop cadence.
/// </summary>
[GlobalClass]
public partial class CareStrokeComponent : Node
{
    private const float ContactSlopPixels = 8.0f;

    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public CareInteractionProfile Profile { get; set; } = null!;

    private IRandomSource? _favoriteRandom;
    private bool _held;
    private bool _hadPetContact;
    private Vector2 _cursor;
    private Vector2 _previousPetCursor;

    public bool IsInitialized { get; private set; }
    public bool LastContactValid { get; private set; }
    public bool IsPetRubbing { get; private set; }
    public bool IsTickleContact { get; private set; }
    public bool PetCompletedThisTick { get; private set; }
    public bool TickleHopRequested { get; private set; }
    public bool TickleBecameAngryThisTick { get; private set; }
    public bool TickleCooldownResetThisTick { get; private set; }
    public TickleDisposition TickleDisposition => Pipeline.TickleDisposition;
    public BuddyPart FavoritePart { get; private set; } = BuddyPart.Head;
    public BuddyPart? ContactPart { get; private set; }
    public Vector2 Cursor => _cursor;
    public bool IsHeld => _held;
    public long ValidContactTicks { get; private set; }
    public int FavoriteSelectionCount { get; private set; }

    public void Initialize(ulong favoriteSeed = 0xC4A3_2026UL)
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException(
                "CareStrokeComponent requires an initialized pipeline and valid care profile.");
        }

        _favoriteRandom = new SeededRandomSource(favoriteSeed);
        Pipeline.ToolChanged += OnToolChanged;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (IsInitialized && GodotObject.IsInstanceValid(Pipeline))
            Pipeline.ToolChanged -= OnToolChanged;
    }

    /// <summary>Latest pointer state in sandbox coordinates; held until replaced.</summary>
    public void SetStroke(bool held, Vector2 worldPoint)
    {
        _held = held;
        _cursor = worldPoint;
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick(double delta)
    {
        RequireInitialized();
        LastContactValid = false;
        IsPetRubbing = false;
        IsTickleContact = false;
        PetCompletedThisTick = false;
        TickleHopRequested = false;
        TickleBecameAngryThisTick = false;
        TickleCooldownResetThisTick = false;
        ContactPart = null;

        ToolId selected = Pipeline.SelectedTool;
        PuppetPartBody? part = null;
        bool valid = _held && TryFindContactPart(_cursor, out part);
        if (valid)
        {
            LastContactValid = true;
            ValidContactTicks++;
            ContactPart = (BuddyPart)(int)part!.PartId;
        }

        if (selected == ToolId.Pet && valid)
        {
            IsPetRubbing = true;
            double distance = _hadPetContact
                ? Math.Min(_cursor.DistanceTo(_previousPetCursor), Profile.MaximumStrokeDistancePerTick)
                : 0.0;
            bool favorite = ContactPart == FavoritePart;
            PetCareResult result = Pipeline.AccumulatePet(distance, favorite, delta);
            PetCompletedThisTick = result.Completed;
            _previousPetCursor = _cursor;
            _hadPetContact = true;
        }
        else
        {
            _hadPetContact = false;
        }

        bool tickleValid = selected == ToolId.Tickle && valid;
        IsTickleContact = tickleValid;
        TickleCareResult tickle = Pipeline.TickTickle(tickleValid, delta);
        TickleHopRequested = tickle.HopRequested;
        TickleBecameAngryThisTick = tickle.BecameAngry;
        TickleCooldownResetThisTick = tickle.CooldownReset;
    }

    private void OnToolChanged(ToolId previous, ToolId selected)
    {
        _hadPetContact = false;
        if (selected != ToolId.Pet || _favoriteRandom is null)
            return;

        FavoritePart = (BuddyPart)_favoriteRandom.NextInt(0, 6);
        FavoriteSelectionCount++;
    }

    private bool TryFindContactPart(Vector2 world, out PuppetPartBody? selected)
    {
        selected = null;
        float bestNormalizedDistance = float.PositiveInfinity;
        IReadOnlyList<PuppetPartBody> parts = Pipeline.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            PuppetPartBody part = parts[index];
            float reach = part.Radius + ContactSlopPixels;
            float distanceSquared = part.GlobalPosition.DistanceSquaredTo(world);
            if (distanceSquared > reach * reach)
                continue;

            float normalized = distanceSquared / (reach * reach);
            if (normalized < bestNormalizedDistance)
            {
                bestNormalizedDistance = normalized;
                selected = part;
            }
        }

        return selected is not null;
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("CareStrokeComponent used before initialization.");
    }
}
