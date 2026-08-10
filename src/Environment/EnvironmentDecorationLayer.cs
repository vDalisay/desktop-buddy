using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.Environment;

public partial class EnvironmentDecorationLayer : Node3D
{
    private readonly Dictionary<PlacedDecorationId, EnvironmentDecorationPresenter> _presenters = [];
    private EnvironmentProgressState _state = null!;
    private BoundaryController _boundaries = null!;
    private EnvironmentLayout _visibleLayout = new();

    public void Configure(EnvironmentProgressState state, BoundaryController boundaries)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _boundaries = boundaries ?? throw new ArgumentNullException(nameof(boundaries));
    }

    public override void _Ready()
    {
        _state.Changed += Rebuild;
        _boundaries.LayoutApplied += OnLayoutApplied;
        Rebuild();
    }

    public override void _ExitTree()
    {
        _state.Changed -= Rebuild;
        _boundaries.LayoutApplied -= OnLayoutApplied;
    }

    public bool TryHit(in CanonicalRoomPosition point, out PlacedDecorationId instanceId)
    {
        float roomWidth = Math.Max(1f, (float)_boundaries.CurrentLayout.RoomWidth);
        float roomHeight = Math.Max(1f, (float)_boundaries.CurrentLayout.RoomHeight);
        PlacedDecoration? best = null;
        foreach (PlacedDecoration placed in _visibleLayout.Decorations)
        {
            EnvironmentDecorationResource? resource = EnvironmentDecorationRegistry.Find(placed.DefinitionId);
            if (resource is null) continue;
            bool wallpaper = placed.RenderBand == DecorationRenderBand.Wallpaper;
            float halfX = wallpaper ? .5f : resource.VisualSize.X / roomWidth * .5f;
            float halfY = wallpaper ? .5f : resource.VisualSize.Y / roomHeight * .5f;
            float centerX = wallpaper ? .5f : placed.Position.X;
            float centerY = wallpaper ? .5f : placed.Position.Y;
            if (Math.Abs(point.X - centerX) > halfX || Math.Abs(point.Y - centerY) > halfY) continue;
            if (!best.HasValue || EnvironmentDecorationPresenter.ZFor(placed.RenderBand) >
                EnvironmentDecorationPresenter.ZFor(best.Value.RenderBand)) best = placed;
        }
        instanceId = best?.InstanceId ?? default;
        return best.HasValue;
    }

    /// <summary>The layout currently on screen, which is the working preview while editing.</summary>
    internal EnvironmentLayout VisibleLayout => _visibleLayout;

    public void Preview(EnvironmentLayout layout) => Rebuild(layout);

    private void Rebuild() => Rebuild(_state.Layout);

    private void Rebuild(EnvironmentLayout layout)
    {
        _visibleLayout = layout;
        foreach (EnvironmentDecorationPresenter presenter in _presenters.Values) presenter.QueueFree();
        _presenters.Clear();
        float width = Math.Max(1f, (float)_boundaries.CurrentLayout.RoomWidth);
        float height = Math.Max(1f, (float)_boundaries.CurrentLayout.RoomHeight);
        Vector2 roomSize = new(width, height);
        foreach (PlacedDecoration placed in layout.Decorations)
        {
            EnvironmentDecorationResource? resource = EnvironmentDecorationRegistry.Find(placed.DefinitionId);
            if (resource is null) continue;
            var presenter = new EnvironmentDecorationPresenter { Name = $"Decoration_{placed.InstanceId}" };
            AddChild(presenter);
            presenter.Position = new Vector3(placed.Position.X * width, -placed.Position.Y * height, 0);
            presenter.Configure(placed, resource, roomSize);
            _presenters[placed.InstanceId] = presenter;
        }
    }

    private void OnLayoutApplied(DesktopBuddy.Domain.Physics.RoomLayout layout, Rect2 bounds) => Rebuild();
}
