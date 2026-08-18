using System;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Work-only typing feedback. The sound is generated in memory so the demo has no placeholder
/// asset dependency, and it reads the machine-local Work mute preference at playback time so
/// the setting never suppresses unrelated SFX.
/// </summary>
public partial class WorkCompanionView
{
    private const int WorkTypingMixRate = 22_050;
    private const double WorkTypingSeconds = 0.028;

    private AudioStreamPlayer? _workTypingPlayer;
    private AudioStreamWav? _workTypingCue;
    private int _workTypingPitchStep;

    /// <summary>
    /// Plays one short mechanical key tick for a batch of observed keyboard activity. Native
    /// hooks can deliver several presses before the main thread drains them; one cue per drain
    /// keeps rapid real typing readable instead of creating an unbounded wall of overlapping
    /// voices.
    /// </summary>
    public void NotifyTyping(long count = 1)
    {
        if (count <= 0 || _sandbox is null || !GodotObject.IsInstanceValid(_sandbox))
            return;

        // MuteInWorkMode is the legacy persisted field name. Its shipping UI now explicitly
        // defines the value as "Mute Work Typing"; retaining the key keeps old settings valid.
        if (_sandbox.Shell.CurrentLocalSettings.MuteInWorkMode)
            return;

        EnsureTypingAudio();
        if (_workTypingPlayer is null || !GodotObject.IsInstanceValid(_workTypingPlayer))
            return;

        // Skip rather than restart a still-playing 28 ms tick. This bounds audio work during
        // key-repeat bursts while still producing a steady mechanical cadence at normal rates.
        if (_workTypingPlayer.Playing)
            return;

        _workTypingPitchStep = (_workTypingPitchStep + (int)Math.Min(count, 3L)) % 3;
        _workTypingPlayer.PitchScale = _workTypingPitchStep switch
        {
            0 => 0.96f,
            1 => 1.00f,
            _ => 1.04f,
        };
        _workTypingPlayer.Stream = _workTypingCue;
        _workTypingPlayer.Play();
    }

    private void EnsureTypingAudio()
    {
        if (_workTypingPlayer is not null && GodotObject.IsInstanceValid(_workTypingPlayer))
            return;

        _workTypingCue ??= BuildTypingCue();
        _workTypingPlayer = new AudioStreamPlayer
        {
            Name = "WorkTypingAudio",
            ProcessMode = ProcessModeEnum.Always,
            Bus = AudioMix.Sfx,
            VolumeDb = -18.0f,
            MaxPolyphony = 1,
            Stream = _workTypingCue,
        };
        AddChild(_workTypingPlayer);
    }

    private static AudioStreamWav BuildTypingCue()
    {
        int samples = Math.Max(1, (int)Math.Round(WorkTypingSeconds * WorkTypingMixRate));
        var data = new byte[samples * 2];
        for (int sample = 0; sample < samples; sample++)
        {
            double progress = sample / (double)samples;
            double envelope = Math.Pow(1.0 - progress, 4.0);

            // A tiny deterministic noise transient plus a high mechanical resonance reads as a
            // key switch without requiring a recorded placeholder clip.
            int hash = unchecked((sample * 1_103_515_245) + 12_345);
            double noise = (((hash >> 16) & 0x7fff) / 16_384.0) - 1.0;
            double resonance = Math.Sin(Math.Tau * 1_650.0 * sample / WorkTypingMixRate);
            double normalized = Math.Clamp(((noise * 0.07) + (resonance * 0.05)) * envelope, -1.0, 1.0);
            short pcm = (short)Math.Round(normalized * short.MaxValue);
            data[sample * 2] = (byte)(pcm & 0xff);
            data[sample * 2 + 1] = (byte)((pcm >> 8) & 0xff);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = WorkTypingMixRate,
            Stereo = false,
            Data = data,
        };
    }
}
