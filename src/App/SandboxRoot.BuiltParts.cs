using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Persistence;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    private readonly Dictionary<SandboxPartId, SandboxPartBody> _builtParts = [];
    private readonly List<SandboxPartId> _builtPartOrder = [];

    /// <summary>The parts physically standing in the active room, keyed by their durable identity.</summary>
    public IReadOnlyDictionary<SandboxPartId, SandboxPartBody> BuiltParts => _builtParts;

    /// <summary>
    /// Rebuilds the room's parts from the active Scene's sandbox document. Parts hold no durable
    /// runtime state, so a Scene switch, a cast change or a restart all take the same path: drop
    /// every live body and re-place the document's parts at rest.
    /// </summary>
    private void ComposeBuiltParts()
    {
        ClearBuiltParts();
        if (SceneProgress is not { } scenes)
            return;

        SandboxDocument document = scenes.ActiveSandbox;
        foreach (PlacedSandboxPart part in document.Parts)
            SpawnBuiltPart(part);
    }

    private void ClearBuiltParts()
    {
        foreach (SandboxPartBody body in _builtParts.Values)
        {
            if (!GodotObject.IsInstanceValid(body))
                continue;
            body.GetParent()?.RemoveChild(body);
            body.QueueFree();
        }
        _builtParts.Clear();
        _builtPartOrder.Clear();
    }

    /// <summary>Places one just-built part into the live room.</summary>
    public void PlaceBuiltPart(PlacedSandboxPart part) => SpawnBuiltPart(part);

    /// <summary>Drops the body of a part the player removed. The document owns the removal itself.</summary>
    public void RemoveBuiltPartBody(SandboxPartId partId) => RemoveBuiltPart(partId);

    /// <summary>
    /// The part under a world point, topmost first, so removing picks what the player sees rather
    /// than what the physics server happens to report while the room is paused.
    /// </summary>
    public bool TryPickBuiltPart(Vector2 world, out SandboxPartId partId)
    {
        partId = default;
        for (int index = _builtPartOrder.Count - 1; index >= 0; index--)
        {
            SandboxPartId candidate = _builtPartOrder[index];
            if (!_builtParts.TryGetValue(candidate, out SandboxPartBody? body) ||
                !GodotObject.IsInstanceValid(body) ||
                !body!.ContainsPoint(world))
            {
                continue;
            }
            partId = candidate;
            return true;
        }
        return false;
    }

    private SandboxPartBody? SpawnBuiltPart(PlacedSandboxPart part)
    {
        ArgumentNullException.ThrowIfNull(part);
        if (!SandboxPartCatalogue.TryGet(part.DefinitionId, out SandboxPartDefinition definition))
        {
            Diagnostics.Log.Error("Sandbox", $"Scene part {part.PartId} names unknown definition '{part.DefinitionId}'.");
            return null;
        }

        RemoveBuiltPart(part.PartId);
        var body = new SandboxPartBody { Name = $"SandboxPart_{part.PartId.ToString()[..8]}" };
        body.Configure(part, definition);
        body.Position = ScenePlacementWorldPosition(part.Position);
        AddChild(body);
        _builtParts[part.PartId] = body;
        _builtPartOrder.Add(part.PartId);
        return body;
    }

    private void RemoveBuiltPart(SandboxPartId partId)
    {
        _builtPartOrder.Remove(partId);
        if (!_builtParts.Remove(partId, out SandboxPartBody? body) || !GodotObject.IsInstanceValid(body))
            return;
        body!.GetParent()?.RemoveChild(body);
        body.QueueFree();
    }

    /// <summary>
    /// Captures where the parts actually came to rest, so a Scene switch or a restart reopens the
    /// room as the player left it rather than as they first placed it.
    /// </summary>
    private void CaptureBuiltPartAnchors(SceneProgressCoordinator scenes)
    {
        if (_builtParts.Count == 0)
            return;

        Rect2 bounds = Boundaries.InnerBounds;
        float width = Math.Max(1.0f, bounds.Size.X);
        float height = Math.Max(1.0f, bounds.Size.Y);
        SandboxDocument document = scenes.ActiveSandbox;

        foreach ((SandboxPartId partId, SandboxPartBody body) in _builtParts)
        {
            if (!GodotObject.IsInstanceValid(body))
                continue;
            var position = new CanonicalRoomPosition(
                Mathf.Clamp((body.GlobalPosition.X - bounds.Position.X) / width, 0.0f, 1.0f),
                Mathf.Clamp((body.GlobalPosition.Y - bounds.Position.Y) / height, 0.0f, 1.0f));
            document.Move(partId, position, body.RotationDegrees);
        }
    }
}
