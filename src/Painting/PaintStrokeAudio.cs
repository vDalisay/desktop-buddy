using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.Painting;

/// <summary>Which stroke sound a paint tool makes while the button is held.</summary>
public enum PaintStrokeSound { None, Draw, Spray }

/// <summary>
/// The held-stroke sound shared by Paint Buddy and Paint Room (owner instruction 2026-08-19:
/// the two painters stay unified). Both editors call <see cref="Set"/> every frame with the
/// sound their current gesture wants; this node owns starting, looping and stopping.
///
/// <para>The pen/brush loop resumes where it left off, so a series of short strokes reads as one
/// continuous drawing sound, and re-pitches on every wrap. The spray picks either clip at random
/// with a wide pitch wobble each time, so a long hold never sounds like one clip on repeat.</para>
/// </summary>
public partial class PaintStrokeAudio : Node
{
    /// <summary>Scale, not a range: each loop lands between 1/1.06 and 1.06.</summary>
    private const float DrawPitchScale = 1.06f;
    /// <summary>
    /// Much wider than the draw wobble: back-to-back sprays should not sound alike. The long
    /// clip is nearly two seconds of broadband hiss, where a small shift reads as no shift at
    /// all, so this range is deliberately extreme.
    /// </summary>
    private const float SprayPitchScale = 1.7f;
    private const double SprayGapSeconds = 1.0;

    private readonly RandomNumberGenerator _random = new();
    private AudioStreamPlayer _draw = null!;
    private AudioStreamPlayer _spray = null!;
    private AudioStream?[] _sprayClips = [];
    private float _drawPosition;
    private double _sprayGapRemaining;
    private PaintStrokeSound _current;

    public static PaintStrokeSound For(PaintTool tool) => tool switch
    {
        PaintTool.Pen or PaintTool.Brush => PaintStrokeSound.Draw,
        PaintTool.Spray => PaintStrokeSound.Spray,
        _ => PaintStrokeSound.None,
    };

    public static PaintStrokeSound For(EnvironmentPaintTool tool) => tool switch
    {
        EnvironmentPaintTool.Pen or EnvironmentPaintTool.Brush => PaintStrokeSound.Draw,
        EnvironmentPaintTool.Spray => PaintStrokeSound.Spray,
        _ => PaintStrokeSound.None,
    };

    public override void _Ready()
    {
        // The editors run inside a paused tree, so the players must tick regardless.
        ProcessMode = ProcessModeEnum.Always;
        _draw = AddPlayer("PaintDrawLoopPlayer");
        // The pencil loop sits under the spray at the shared level: +2.3 dB is 30% louder.
        _draw.VolumeDb += 2.3f;
        _draw.Stream = LoadClip("Drawing_Loop.mp3");
        // The wrap is ours, not the stream's: a self-looping stream never raises Finished, and
        // the per-loop pitch change hangs off that signal.
        if (_draw.Stream is AudioStreamMP3 drawMp3)
            drawMp3.Loop = false;
        _draw.Finished += OnDrawFinished;
        _spray = AddPlayer("PaintSprayPlayer");
        _sprayClips = [LoadClip("SprayPaint_1.mp3"), LoadClip("SprayPaint_2.mp3")];
        _spray.Finished += OnSprayFinished;
        _random.Randomize();
    }

    /// <summary>Starts, keeps, or ends the stroke sound. Safe to call every frame.</summary>
    public void Set(PaintStrokeSound sound)
    {
        if (sound == _current)
            return;

        Stop();
        _current = sound;
        switch (sound)
        {
            case PaintStrokeSound.Draw:
                _draw.PitchScale = RandomPitch(DrawPitchScale);
                // Resuming from the stored position keeps a burst of short strokes sounding
                // like one continuous pencil rather than the same attack over and over.
                _draw.Play(_drawPosition);
                break;
            case PaintStrokeSound.Spray:
                PlayNextSprayClip();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_current != PaintStrokeSound.Spray || _sprayGapRemaining <= 0.0)
            return;

        _sprayGapRemaining -= Math.Max(0.0, delta);
        if (_sprayGapRemaining <= 0.0)
            PlayNextSprayClip();
    }

    public override void _ExitTree() => Stop();

    private void Stop()
    {
        if (_current == PaintStrokeSound.Draw && _draw.Playing)
            _drawPosition = _draw.GetPlaybackPosition();
        _draw.Stop();
        _spray.Stop();
        _sprayGapRemaining = 0.0;
        _current = PaintStrokeSound.None;
    }

    private void OnDrawFinished()
    {
        if (_current != PaintStrokeSound.Draw)
            return;
        _drawPosition = 0.0f;
        _draw.PitchScale = RandomPitch(DrawPitchScale);
        _draw.Play();
    }

    private void OnSprayFinished()
    {
        if (_current == PaintStrokeSound.Spray)
            _sprayGapRemaining = SprayGapSeconds;
    }

    private void PlayNextSprayClip()
    {
        _sprayGapRemaining = 0.0;
        AudioStream? clip = _sprayClips[_random.RandiRange(0, _sprayClips.Length - 1)];
        if (clip is null)
            return;
        _spray.Stream = clip;
        _spray.PitchScale = RandomPitch(SprayPitchScale);
        _spray.Play();
    }

    private float RandomPitch(float scale) => _random.RandfRange(1.0f / scale, scale);

    private AudioStreamPlayer AddPlayer(string name)
    {
        var player = new AudioStreamPlayer
        {
            Name = name,
            ProcessMode = ProcessModeEnum.Always,
            Bus = AudioMix.Sfx,
            VolumeDb = -8.0f,
            MaxPolyphony = 1,
        };
        AddChild(player);
        return player;
    }

    /// <summary>
    /// The direct file load is the fallback for a build whose import metadata has not been
    /// generated yet, so a freshly dropped .mp3 plays without a reimport (same rule as the UI
    /// feedback layer).
    /// </summary>
    private static AudioStream? LoadClip(string fileName)
    {
        string resourcePath = $"res://assets/sfx/paint/{fileName}";
        if (ResourceLoader.Exists(resourcePath) &&
            ResourceLoader.Load<AudioStream>(resourcePath) is { } imported)
        {
            return imported;
        }

        string absolute = ProjectSettings.GlobalizePath(resourcePath);
        return FileAccess.FileExists(absolute) ? AudioStreamMP3.LoadFromFile(absolute) : null;
    }
}
