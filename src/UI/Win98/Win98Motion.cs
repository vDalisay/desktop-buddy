using System;
using DesktopBuddy.Domain.Persistence;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Small named presentation operations for the Modern Win98 preference. These helpers never move
/// native windows, change minimum sizes, defer input acknowledgement, or own semantic visibility.
/// Callers keep their normal state transitions and layer a short visual response on top.
/// </summary>
public static class Win98Motion
{
    public static Tween? Pulse(Control control, LocalSettingsSave settings, double seconds = 0.12)
    {
        ArgumentNullException.ThrowIfNull(control);
        KillOwnedTween(control);
        Reset(control);
        if (!Win98MotionPolicy.Allows(settings) || !control.IsInsideTree())
            return null;

        double duration = Math.Clamp(seconds, 0.08, 0.16);
        Vector2 originalScale = Vector2.One;
        control.PivotOffset = control.Size * 0.5f;
        control.Scale = new Vector2(1.025f, 1.025f);
        Tween tween = control.CreateTween();
        tween.SetParallel(true);
        tween.SetTrans(Tween.TransitionType.Quad);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "scale", originalScale, duration);
        tween.TweenProperty(control, "modulate", Colors.White, duration);
        Remember(control, tween);
        tween.Finished += () =>
        {
            if (GodotObject.IsInstanceValid(control))
                Reset(control);
        };
        return tween;
    }

    public static Tween? Reveal(Control control, LocalSettingsSave settings, double seconds = 0.11)
    {
        ArgumentNullException.ThrowIfNull(control);
        KillOwnedTween(control);
        Reset(control);
        if (!Win98MotionPolicy.Allows(settings) || !control.IsInsideTree())
            return null;

        double duration = Math.Clamp(seconds, 0.08, 0.16);
        control.Modulate = new Color(1f, 1f, 1f, 0.25f);
        Tween tween = control.CreateTween();
        tween.SetTrans(Tween.TransitionType.Quad);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "modulate", Colors.White, duration);
        Remember(control, tween);
        tween.Finished += () =>
        {
            if (GodotObject.IsInstanceValid(control))
                Reset(control);
        };
        return tween;
    }

    public static void Stop(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        KillOwnedTween(control);
        Reset(control);
    }

    private static void Remember(Control control, Tween tween) =>
        control.SetMeta("desktop_buddy_win98_motion_tween", tween);

    private static void KillOwnedTween(Control control)
    {
        if (!control.HasMeta("desktop_buddy_win98_motion_tween"))
            return;
        Variant value = control.GetMeta("desktop_buddy_win98_motion_tween");
        if (value.AsGodotObject() is Tween tween && GodotObject.IsInstanceValid(tween))
            tween.Kill();
        control.RemoveMeta("desktop_buddy_win98_motion_tween");
    }

    private static void Reset(Control control)
    {
        control.Scale = Vector2.One;
        control.Modulate = Colors.White;
    }
}
