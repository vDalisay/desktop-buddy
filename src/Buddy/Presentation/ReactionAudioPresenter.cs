using System;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>Plays short original synthesized robot chirps from semantic reactions.</summary>
[GlobalClass]
public partial class ReactionAudioPresenter : Node
{
    private const int MixRate = 22050;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public ReactionProfile Profile { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !GodotObject.IsInstanceValid(Profile) ||
            !GodotObject.IsInstanceValid(Player))
            throw new InvalidOperationException("ReactionAudioPresenter requires pipeline, profile, and player.");
        Pipeline.ImpactAccepted += OnImpact;
        Pipeline.CareAwarded += OnCare;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
        {
            Pipeline.ImpactAccepted -= OnImpact;
            Pipeline.CareAwarded -= OnCare;
        }
        if (GodotObject.IsInstanceValid(Player))
        {
            Player.Stop();
            Player.Stream = null;
        }
    }

    private void OnImpact(AcceptedImpact impact)
    {
        bool glove = impact.ContentId == (int)ToolId.BoxingGlove;
        float normalized = Mathf.Clamp(impact.Pain / 100.0f, 0.0f, 1.0f);
        PlayChirp(
            Profile.PainChirpHz * Mathf.Lerp(1.15f, 0.72f, normalized),
            glove ? Profile.GloveImpactAmplitude : 7_000.0f);
    }
    private void OnCare(CareKind kind) => PlayChirp(Profile.CareChirpHz, 7_000.0f);

    private void PlayChirp(float frequency, float amplitude)
    {
        int samples = Math.Max(1, (int)(MixRate * Profile.ChirpSeconds));
        var bytes = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            double envelope = 1.0 - (double)i / samples;
            short value = (short)(Math.Sin(Math.Tau * frequency * i / MixRate) * envelope * amplitude);
            bytes[i * 2] = (byte)(value & 0xff);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xff);
        }
        Player.Stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = bytes,
        };
        Player.Play();
    }
}
