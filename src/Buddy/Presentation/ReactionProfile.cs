using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>Empirical M3 reaction, face, resistance, and robot-chirp tuning.</summary>
[GlobalClass]
public partial class ReactionProfile : GameResource
{
    [Export(PropertyHint.Range, "0,5,0.01")] public double PainFaceSeconds { get; set; } = 0.45;
    [Export(PropertyHint.Range, "0,5,0.01")] public double DelightFaceSeconds { get; set; } = 0.6;
    [Export(PropertyHint.Range, "0,5,0.01")] public double AcuteFearSeconds { get; set; } = 1.0;
    [Export(PropertyHint.Range, "0,10,0.01")] public double LearnedThreatFaceTailSeconds { get; set; } = 5.0;
    [Export(PropertyHint.Range, "0,5,0.01")] public double PetCompletionFaceSeconds { get; set; } = 0.75;

    /// <summary>
    /// How long the buddy laughs after catching a thrown ball out of the air. Deliberately
    /// longer than the ordinary delight blip: this is the reward for a good throw and it has
    /// to be unmistakable (owner instruction 2026-07-27).
    /// </summary>
    [Export(PropertyHint.Range, "0,8,0.01")] public double LaughFaceSeconds { get; set; } = 1.8;
    [Export(PropertyHint.Range, "0,1,0.01")] public float FearfulResistance { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float WaryResistance { get; set; } = 0.5f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float AcuteFearResistance { get; set; } = 0.65f;
    [Export(PropertyHint.Range, "80,2000,1")] public float PainChirpHz { get; set; } = 180.0f;
    [Export(PropertyHint.Range, "80,2000,1")] public float CareChirpHz { get; set; } = 620.0f;
    [Export(PropertyHint.Range, "1000,30000,100")] public float GloveImpactAmplitude { get; set; } = 12_000.0f;
    [Export(PropertyHint.Range, "0.02,0.5,0.01")] public float ChirpSeconds { get; set; } = 0.09f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (PainFaceSeconds < 0 || DelightFaceSeconds < 0 || AcuteFearSeconds < 0 ||
            LearnedThreatFaceTailSeconds < 0 || LaughFaceSeconds < 0 ||
            PetCompletionFaceSeconds < 0)
            errors.Add("reaction durations must be non-negative");
        if (FearfulResistance is < 0 or > 1 || WaryResistance is < 0 or > 1 || AcuteFearResistance is < 0 or > 1)
            errors.Add("resistance strengths must be within [0,1]");
        if (PainChirpHz <= 0 || CareChirpHz <= 0 || ChirpSeconds <= 0 || GloveImpactAmplitude <= 0)
            errors.Add("chirp tuning must be positive");
        return errors;
    }
}
