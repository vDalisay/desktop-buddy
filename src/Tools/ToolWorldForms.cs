using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// A tool's world form: the body it has when it is lying in the room rather than in the player's
/// hand. Most tools drop as the very body the player was holding; the guns, the sprayer and the
/// feather have no cursor-tethered body of their own, so they carry an authored drop-only profile.
///
/// <para>One place, because two callers need the same answer: dropping a tool
/// (<see cref="DroppedToolInteractionComponent"/>) and putting one in a built room as a part
/// (owner 2026-09-12).</para>
/// </summary>
public static class ToolWorldForms
{
    private static readonly string[] DropFormPaths =
    [
        "res://data/tools/drop_form_pistol.tres",
        "res://data/tools/drop_form_shotgun.tres",
        "res://data/tools/drop_form_nerf_blaster.tres",
        "res://data/tools/drop_form_fire_sprayer.tres",
        "res://data/tools/drop_form_tickle.tres",
    ];

    private static CursorToolProfile[]? _dropForms;

    /// <summary>The form <paramref name="tool"/> takes on the ground, or null when it has none.</summary>
    public static CursorToolProfile? For(CursorToolController? tools, ToolId tool)
    {
        if (GodotObject.IsInstanceValid(tools) && tools!.ProfileOf(tool) is { } held &&
            held.WorldDrop is not null && GodotObject.IsInstanceValid(held.WorldDrop))
        {
            return held;
        }
        return DropForm(tool);
    }

    /// <summary>The authored drop-only profiles, loaded once.</summary>
    public static CursorToolProfile? DropForm(ToolId tool)
    {
        if (_dropForms is null)
        {
            var forms = new System.Collections.Generic.List<CursorToolProfile>(DropFormPaths.Length);
            foreach (string path in DropFormPaths)
            {
                if (GD.Load(path) is CursorToolProfile form && GodotObject.IsInstanceValid(form))
                    forms.Add(form);
                else
                    GD.PushWarning($"Drop form missing or malformed: {path}");
            }
            _dropForms = [.. forms];
        }

        foreach (CursorToolProfile form in _dropForms)
        {
            if (GodotObject.IsInstanceValid(form) && form.Tool == tool)
                return form;
        }
        return null;
    }
}
