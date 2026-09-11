using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.UI;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Editing what is already in the room: select, drag, rotate, freeze, duplicate, delete and tune
/// one part at a time while the room is paused (NF-3). Every edit goes to the Scene's sandbox
/// document first and the live body follows it, so what is saved is always what is shown.
/// </summary>
public partial class BuildModeController
{
    private const float RotateStepDegrees = 15.0f;
    private const float FineRotateStepDegrees = 5.0f;
    private const float DuplicateOffsetPixels = 24.0f;

    private SandboxPartId? _selectedPart;
    private bool _dragging;
    private Vector2 _dragOffset;
    private readonly List<(SandboxPartBody Body, Vector2 Offset)> _dragGroup = [];

    private Label? _propertiesTitle;
    private HSlider? _massSlider;
    private HSlider? _bounceSlider;
    private HSlider? _gravitySlider;
    private Label? _massReadout;
    private Label? _bounceReadout;
    private Label? _gravityReadout;
    private HSlider? _strengthSlider;
    private HSlider? _intervalSlider;
    private Label? _strengthReadout;
    private Label? _intervalReadout;
    private CheckBox? _frozenToggle;
    private Button? _duplicateButton;
    private Button? _deleteButton;
    private Button? _resetButton;
    private Button? _unlinkButton;

    /// <summary>The part being edited in the room, if any.</summary>
    public SandboxPartId? SelectedPlacedPart => _selectedPart;

    /// <summary>Selects the part under a world point; returns false and clears the selection on empty space.</summary>
    public bool SelectPlacedPartAt(Vector2 world)
    {
        if (!_sandbox.TryPickBuiltPart(world, out SandboxPartId partId))
        {
            SelectPlacedPart(null);
            return false;
        }
        SelectPlacedPart(partId);
        return true;
    }

    /// <summary>Selects one placed part by identity; false if it is not standing in the room.</summary>
    public bool SelectPlaced(SandboxPartId partId)
    {
        SelectPlacedPart(partId);
        return _selectedPart == partId;
    }

    /// <summary>Moves the selected part so its centre sits at <paramref name="world"/>, and saves it.</summary>
    public bool MoveSelectedPartTo(Vector2 world)
    {
        if (!TrySelectedBody(out SandboxPartBody body))
            return false;
        Vector2 delta = ClampToRoom(world) - body.GlobalPosition;
        List<SandboxPartBody> group = LinkedGroup();
        foreach (SandboxPartBody member in group)
            member.GlobalPosition += delta;
        return CommitGroupTransform(group);
    }

    public bool RotateSelectedPart(float degrees)
    {
        if (!TrySelectedBody(out SandboxPartBody body))
            return false;
        // A hinged or welded assembly turns as one, about the part the player is holding.
        Vector2 centre = body.GlobalPosition;
        float radians = Mathf.DegToRad(degrees);
        List<SandboxPartBody> group = LinkedGroup();
        foreach (SandboxPartBody member in group)
        {
            member.GlobalPosition = centre + (member.GlobalPosition - centre).Rotated(radians);
            member.RotationDegrees = PlacedSandboxPart.NormalizeRotation(member.RotationDegrees + degrees);
        }
        bool saved = CommitGroupTransform(group);
        if (saved)
            SetStatus($"Rotated to {body.RotationDegrees:0}°.");
        return saved;
    }

    /// <summary>Copies the selected part, its rotation and its tuning, a little down and to the right.</summary>
    public SandboxPartId? DuplicateSelectedPart()
    {
        if (!TrySelectedPart(out PlacedSandboxPart part, out SandboxPartBody body))
            return null;

        SandboxDocument document = _scenes.ActiveSandbox;
        Vector2 target = ClampToRoom(body.GlobalPosition + new Vector2(DuplicateOffsetPixels, DuplicateOffsetPixels));
        SandboxEditResult added = document.Add(
            part.DefinitionId,
            ToCanonical(target),
            body.RotationDegrees,
            part.Overrides);
        if (!added.Succeeded)
        {
            SetStatus(added.Status == SandboxEditStatus.LimitReached
                ? $"This room already holds {SandboxDocument.MaximumParts} parts."
                : $"Could not duplicate the part ({added.Status}).");
            return null;
        }

        _sandbox.PlaceBuiltPart(added.Part!);
        SelectPlacedPart(added.Part!.PartId);
        SetStatus($"Duplicated. {document.Count} parts in this room.");
        RefreshPartCount();
        return added.Part.PartId;
    }

    public bool DeleteSelectedPart()
    {
        if (_selectedPart is not { } partId)
            return false;
        return RemovePart(partId);
    }

    /// <summary>
    /// Replaces the selected part's tuning. The document clamps every value into its band, and the
    /// body applies what the document kept, so an out-of-range request can never reach physics.
    /// </summary>
    public bool SetSelectedPartOverrides(SandboxPartOverrides overrides)
    {
        if (!TrySelectedPart(out PlacedSandboxPart _, out SandboxPartBody body) || _selectedPart is not { } partId)
            return false;

        SandboxEditResult updated = _scenes.ActiveSandbox.SetOverrides(partId, overrides);
        if (!updated.Succeeded && updated.Status != SandboxEditStatus.NoChange)
        {
            SetStatus($"Could not change the part ({updated.Status}).");
            return false;
        }
        body.ApplyOverrides(updated.Part!.Overrides);
        RefreshProperties();
        return true;
    }

    private void ToggleSelectedFrozen()
    {
        if (TrySelectedPart(out PlacedSandboxPart part, out _))
        {
            SetSelectedPartOverrides(part.Overrides with { Frozen = !part.Overrides.Frozen });
            SetStatus(part.Overrides.Frozen ? "Unfrozen: it will move in Play." : "Frozen in place.");
        }
    }

    private void SelectPlacedPart(SandboxPartId? partId)
    {
        if (_selectedPart is { } previous && _sandbox.BuiltParts.TryGetValue(previous, out SandboxPartBody? old) &&
            GodotObject.IsInstanceValid(old))
        {
            old!.Selected = false;
        }
        _selectedPart = partId;
        _dragging = false;
        if (TrySelectedBody(out SandboxPartBody body))
            body.Selected = true;
        else
            _selectedPart = null;
        RefreshProperties();
    }

    /// <summary>
    /// Mouse and keyboard for the room itself. Runs after the GUI (see <see cref="_UnhandledInput"/>),
    /// so presses on the palette never reach it.
    /// </summary>
    private bool HandleEditInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
            return HandleEditKey(key);

        if (@event is InputEventMouseMotion && _dragging)
        {
            if (TrySelectedBody(out SandboxPartBody dragged))
            {
                dragged.GlobalPosition = ClampToRoom(_sandbox.GetGlobalMousePosition() + _dragOffset);
                foreach ((SandboxPartBody member, Vector2 offset) in _dragGroup)
                    member.GlobalPosition = dragged.GlobalPosition + offset;
                _sandbox.RedrawBuiltLinks();
            }
            return true;
        }

        if (_tool != BuildTool.Parts && HandleLinkInput(@event))
            return true;

        if (@event is not InputEventMouseButton button)
            return false;

        if (!button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.Left && _dragging)
            {
                FinishDrag();
                return true;
            }
            return false;
        }

        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && _selectedPart is not null)
        {
            float step = button.ShiftPressed ? FineRotateStepDegrees : RotateStepDegrees;
            RotateSelectedPart(button.ButtonIndex == MouseButton.WheelUp ? -step : step);
            return true;
        }

        Vector2 world = _sandbox.GetGlobalMousePosition();
        if (!_sandbox.Boundaries.InnerBounds.HasPoint(world))
            return false;

        if (button.ButtonIndex == MouseButton.Right)
        {
            // A pin or rope sits on top of the parts it joins, so it is what a right-click means.
            if (RemoveLinkAt(world))
                return true;
            if (_sandbox.TryPickBuiltPart(world, out _))
                RemovePartAt(world);
            else
                SelectPlacedPart(null);
            return true;
        }
        if (button.ButtonIndex != MouseButton.Left || _tool != BuildTool.Parts)
            return false;

        // A part under the pointer is picked up with everything hinged or welded to it; empty
        // space gets the palette's part.
        if (SelectPlacedPartAt(world) && TrySelectedBody(out SandboxPartBody picked))
        {
            _dragging = true;
            _dragOffset = picked.GlobalPosition - world;
            _dragGroup.Clear();
            foreach (SandboxPartBody member in LinkedGroup())
            {
                if (member != picked)
                    _dragGroup.Add((member, member.GlobalPosition - picked.GlobalPosition));
            }
            SetStatus("Drag to move. Wheel or Q/E rotates, Ctrl+D duplicates, F freezes, Delete removes.");
            return true;
        }
        PlaceSelectedPartAt(world);
        return true;
    }

    private bool HandleEditKey(InputEventKey key)
    {
        BuildTool? chosen = key.Keycode switch
        {
            Key.Key1 => BuildTool.Parts,
            Key.Key2 => BuildTool.Rope,
            Key.Key3 => BuildTool.Hinge,
            Key.Key4 => BuildTool.Weld,
            Key.Key5 => BuildTool.Wire,
            _ => null,
        };
        if (chosen is { } tool)
        {
            SetTool(tool);
            return true;
        }
        // Escape backs out one layer at a time: a half-made rope, then the link tool, then the
        // selection, and only then the Build workspace itself.
        if (key.Keycode == Key.Escape && _ropeStart is not null)
        {
            CancelPendingLink();
            SetStatus("Rope cancelled.");
            return true;
        }
        if (key.Keycode == Key.Escape && _tool != BuildTool.Parts)
        {
            SetTool(BuildTool.Parts);
            return true;
        }
        if (key.Keycode == Key.Escape && _selectedPart is not null)
        {
            SelectPlacedPart(null);
            SetStatus("Nothing selected. Escape again plays.");
            return true;
        }
        if (_selectedPart is null)
            return false;

        float step = key.ShiftPressed ? FineRotateStepDegrees : RotateStepDegrees;
        switch (key.Keycode)
        {
            case Key.Delete or Key.Backspace:
                DeleteSelectedPart();
                return true;
            case Key.Q:
                RotateSelectedPart(-step);
                return true;
            case Key.E:
                RotateSelectedPart(step);
                return true;
            case Key.F:
                ToggleSelectedFrozen();
                return true;
            case Key.D when key.CtrlPressed:
                DuplicateSelectedPart();
                return true;
            default:
                return false;
        }
    }

    private void FinishDrag()
    {
        _dragging = false;
        List<SandboxPartBody> group = LinkedGroup();
        _dragGroup.Clear();
        if (group.Count > 0 && CommitGroupTransform(group))
            SetStatus(group.Count > 1 ? $"Moved {group.Count} linked parts." : "Moved.");
    }

    /// <summary>
    /// The selected part and every part joined to it through hinges and welds: the rigid assembly
    /// that has to move together or its joints would tear on the next Play. Ropes are slack and
    /// do not bind a group.
    /// </summary>
    private List<SandboxPartBody> LinkedGroup()
    {
        var members = new List<SandboxPartBody>();
        if (_selectedPart is not { } root)
            return members;

        var seen = new HashSet<SandboxPartId> { root };
        var queue = new Queue<SandboxPartId>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            SandboxPartId current = queue.Dequeue();
            if (_sandbox.BuiltParts.TryGetValue(current, out SandboxPartBody? body) && GodotObject.IsInstanceValid(body))
                members.Add(body!);
            foreach (SandboxLink link in _scenes.ActiveSandbox.Links)
            {
                if (link.Kind == SandboxLinkKind.Rope || link.B.IsWorld || !link.Touches(current))
                    continue;
                SandboxPartId other = link.A.PartId == current ? link.B.PartId : link.A.PartId;
                if (seen.Add(other))
                    queue.Enqueue(other);
            }
        }
        return members;
    }

    /// <summary>
    /// Saves where every part of a moved group now rests, carries room hinges along with the part
    /// they pin, and rebuilds the live joints from those resting places.
    /// </summary>
    private bool CommitGroupTransform(List<SandboxPartBody> group)
    {
        SandboxDocument document = _scenes.ActiveSandbox;
        var moved = new HashSet<SandboxPartId>();
        foreach (SandboxPartBody body in group)
        {
            SandboxEditResult result = document.Move(body.PartId, ToCanonical(body.GlobalPosition), body.RotationDegrees);
            if (!result.Succeeded && result.Status != SandboxEditStatus.NoChange)
            {
                SetStatus($"Could not move the part ({result.Status}).");
                return false;
            }
            moved.Add(body.PartId);
        }

        foreach (SandboxLink link in document.Links.ToList())
        {
            if (link.Kind != SandboxLinkKind.Hinge || !link.B.IsWorld || !moved.Contains(link.A.PartId) ||
                _sandbox.LinkEndWorld(link.A) is not { } pivot)
            {
                continue;
            }
            CanonicalRoomPosition anchor = ToCanonical(pivot);
            document.ReseatLink(link.LinkId, link.A, SandboxLinkEnd.World(anchor.X, anchor.Y), 0.0f);
        }
        if (document.Links.Count > 0)
            _sandbox.RebuildBuiltLinks();
        return true;
    }

    private bool TrySelectedBody(out SandboxPartBody body)
    {
        body = null!;
        if (_selectedPart is not { } partId ||
            !_sandbox.BuiltParts.TryGetValue(partId, out SandboxPartBody? found) ||
            !GodotObject.IsInstanceValid(found))
        {
            return false;
        }
        body = found!;
        return true;
    }

    private bool TrySelectedPart(out PlacedSandboxPart part, out SandboxPartBody body)
    {
        part = null!;
        if (!TrySelectedBody(out body) ||
            !_scenes.ActiveSandbox.TryGet(_selectedPart!.Value, out PlacedSandboxPart? found) ||
            found is null)
        {
            return false;
        }
        part = found;
        return true;
    }

    private Vector2 ClampToRoom(Vector2 world)
    {
        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        return new Vector2(
            Mathf.Clamp(world.X, bounds.Position.X, bounds.End.X),
            Mathf.Clamp(world.Y, bounds.Position.Y, bounds.End.Y));
    }

    private CanonicalRoomPosition ToCanonical(Vector2 world)
    {
        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        return new CanonicalRoomPosition(
            Mathf.Clamp((world.X - bounds.Position.X) / Math.Max(1.0f, bounds.Size.X), 0.0f, 1.0f),
            Mathf.Clamp((world.Y - bounds.Position.Y) / Math.Max(1.0f, bounds.Size.Y), 0.0f, 1.0f));
    }

    /// <summary>The "Selected part" group: what it is, its tuning, and what can be done to it.</summary>
    private void BuildPropertiesUi(VBoxContainer body)
    {
        var group = new Win98GroupBox { Name = "BuildModeProperties" };
        group.Configure("Selected part");
        body.AddChild(group);

        _propertiesTitle = new Label
        {
            Name = "BuildModePropertiesTitle",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        group.Content.AddChild(_propertiesTitle);

        // Mass is a scale spanning two orders of magnitude, so the slider moves in log space.
        _massSlider = AddPropertySlider(group.Content, "Mass", "How heavy it is, relative to the part's own mass.",
            Mathf.Log(SandboxPartOverrides.MinimumMassScale) / Mathf.Log(10.0f),
            Mathf.Log(SandboxPartOverrides.MaximumMassScale) / Mathf.Log(10.0f),
            0.05, out _massReadout, value => WithOverride(o => o with { MassScale = Mathf.Pow(10.0f, (float)value) }));
        _bounceSlider = AddPropertySlider(group.Content, "Bounce", "How much it springs back after a hit.",
            0.0, 1.0, 0.05, out _bounceReadout, value => WithOverride(o => o with { Bounce = (float)value }));
        _gravitySlider = AddPropertySlider(group.Content, "Gravity", "How strongly it falls. Negative floats upward.",
            SandboxPartOverrides.MinimumGravityScale, SandboxPartOverrides.MaximumGravityScale,
            0.1, out _gravityReadout, value => WithOverride(o => o with { GravityScale = (float)value }));
        // Device settings: each row shows only while its device is selected.
        _strengthSlider = AddPropertySlider(group.Content, "Strength", "How hard the Piston shoves.",
            SandboxPartOverrides.MinimumPistonPush, SandboxPartOverrides.MaximumPistonPush,
            10.0, out _strengthReadout, value => WithOverride(o => o with { PistonPush = (float)value }));
        _intervalSlider = AddPropertySlider(group.Content, "Every", "How often the Timer sends a pulse while it runs.",
            SandboxPartOverrides.MinimumTimerSeconds, SandboxPartOverrides.MaximumTimerSeconds,
            0.1, out _intervalReadout, value => WithOverride(o => o with { TimerSeconds = (float)value }));

        _frozenToggle = new CheckBox
        {
            Name = "BuildModeFrozenToggle",
            Text = "Frozen in place (F)",
            TooltipText = "A frozen part never moves in Play: a floor, a wall, an anchor.",
            FocusMode = Control.FocusModeEnum.All,
        };
        _frozenToggle.Toggled += frozen => WithOverride(o => o with { Frozen = frozen });
        group.Content.AddChild(_frozenToggle);

        var actions = new HBoxContainer { Name = "BuildModePropertiesActions" };
        actions.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        group.Content.AddChild(actions);
        _duplicateButton = Win98Dialog.Action(actions, "Duplicate", () => DuplicateSelectedPart());
        _duplicateButton.Name = "BuildModeDuplicateButton";
        _deleteButton = Win98Dialog.Action(actions, "Delete", () => DeleteSelectedPart());
        _deleteButton.Name = "BuildModeDeleteButton";
        _resetButton = Win98Dialog.Action(actions, "Reset", () => SetSelectedPartOverrides(SandboxPartOverrides.None));
        _resetButton.Name = "BuildModeResetButton";
        _resetButton.TooltipText = "Back to the part's own settings, unfrozen.";
        _unlinkButton = Win98Dialog.Action(actions, "Unlink", () =>
        {
            if (_selectedPart is { } partId && _scenes.ActiveSandbox.RemoveLinksOf(partId) > 0)
            {
                _sandbox.RebuildBuiltLinks();
                SetStatus("Removed every link on this part.");
                RefreshProperties();
            }
        });
        _unlinkButton.Name = "BuildModeUnlinkButton";
        _unlinkButton.TooltipText = "Remove every rope, hinge and weld on this part.";

        RefreshProperties();
    }

    private HSlider AddPropertySlider(
        VBoxContainer column,
        string label,
        string tooltip,
        double minimum,
        double maximum,
        double step,
        out Label readout,
        Action<double> changed)
    {
        var row = new HBoxContainer { Name = $"BuildMode{label}Row" };
        row.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        column.AddChild(row);
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(64, 0) });
        var slider = new HSlider
        {
            Name = $"BuildMode{label}Slider",
            MinValue = minimum,
            MaxValue = maximum,
            Step = step,
            TooltipText = tooltip,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(180, 0),
        };
        slider.ValueChanged += value =>
        {
            UiFeedbackAudioBootstrap.TryPlaySliderTick(this);
            changed(value);
        };
        row.AddChild(slider);
        readout = new Label { CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right };
        row.AddChild(readout);
        return slider;
    }

    private void WithOverride(Func<SandboxPartOverrides, SandboxPartOverrides> change)
    {
        if (TrySelectedPart(out PlacedSandboxPart part, out _))
            SetSelectedPartOverrides(change(part.Overrides));
    }

    /// <summary>Shows the selected part's current tuning without re-triggering the controls.</summary>
    private void RefreshProperties()
    {
        if (_propertiesTitle is null)
            return;

        bool hasPart = TrySelectedPart(out PlacedSandboxPart part, out _);
        SandboxPartDefinition? definition = null;
        if (hasPart && !SandboxPartCatalogue.TryGet(part.DefinitionId, out definition))
            hasPart = false;

        int links = hasPart ? _scenes.ActiveSandbox.Links.Count(link => link.Touches(part.PartId)) : 0;
        _propertiesTitle.Text = hasPart
            ? $"{definition!.DisplayName}, turned {part.RotationDegrees:0}°" +
              (links > 0 ? $", {links} link{(links == 1 ? string.Empty : "s")}" : string.Empty)
            : "Click a part in the room to select it.";
        foreach (Control? control in new Control?[]
                     { _massSlider, _bounceSlider, _gravitySlider, _frozenToggle, _duplicateButton, _deleteButton, _resetButton, _unlinkButton })
        {
            switch (control)
            {
                case Slider slider: slider.Editable = hasPart; break;
                case BaseButton button: button.Disabled = !hasPart; break;
            }
        }
        if (!hasPart)
        {
            _massReadout!.Text = _bounceReadout!.Text = _gravityReadout!.Text = string.Empty;
            _frozenToggle!.SetPressedNoSignal(false);
            _strengthSlider!.GetParent<Control>().Visible = false;
            _intervalSlider!.GetParent<Control>().Visible = false;
            return;
        }

        SandboxPartOverrides overrides = part.Overrides;
        float massScale = overrides.MassFor(definition!) / definition!.Mass;
        float bounce = overrides.BounceFor(definition);
        float gravity = overrides.GravityScaleValue;
        _massSlider!.SetValueNoSignal(Mathf.Log(massScale) / Mathf.Log(10.0f));
        _bounceSlider!.SetValueNoSignal(bounce);
        _gravitySlider!.SetValueNoSignal(gravity);
        _massReadout!.Text = $"×{massScale:0.0#}";
        _bounceReadout!.Text = $"{bounce * 100.0f:0}%";
        _gravityReadout!.Text = $"×{gravity:0.0}";
        _frozenToggle!.SetPressedNoSignal(overrides.Frozen);

        bool piston = definition.Device == SandboxDeviceKind.Piston;
        bool timer = definition.Device == SandboxDeviceKind.Timer;
        _strengthSlider!.GetParent<Control>().Visible = piston;
        _intervalSlider!.GetParent<Control>().Visible = timer;
        _strengthSlider.Editable = piston;
        _intervalSlider.Editable = timer;
        float push = overrides.PistonPushValue;
        float seconds = overrides.TimerSecondsValue;
        _strengthSlider.SetValueNoSignal(push);
        _intervalSlider.SetValueNoSignal(seconds);
        _strengthReadout!.Text = $"{push / SandboxPartOverrides.MaximumPistonPush * 100.0f:0}%";
        _intervalReadout!.Text = $"{seconds:0.0} s";
    }

    private void RefreshPartCount()
    {
        if (_hint is not null)
            _hint.Text = $"{_scenes.ActiveSandbox.Count} of {SandboxDocument.MaximumParts} parts placed.";
    }
}
