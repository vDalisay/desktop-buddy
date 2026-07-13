using System;
using DesktopBuddy.Domain.Mood;
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
        if (!GodotObject.IsInstanceValid(Pipeline)) return;
        Pipeline.ImpactAccepted -= OnImpact;
        Pipeline.CareAwarded -= OnCare;
    }

    private void OnImpact(AcceptedImpact impact) => PlayChirp(Profile.PainChirpHz);
    private void OnCare(CareKind kind) => PlayChirp(Profile.CareChirpHz);

    private void PlayChirp(float frequency)
    {
        int samples = Math.Max(1, (int)(MixRate * Profile.ChirpSeconds));
        var bytes = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            double envelope = 1.0 - (double)i / samples;
            short value = (short)(Math.Sin(Math.Tau * frequency * i / MixRate) * envelope * 7000.0);
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
