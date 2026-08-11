using System;
using DesktopBuddy.Work;
using Godot;

namespace DesktopBuddy.UI;

public enum UiFeedbackCue
{
    Click,
    Confirm,
    Purchase,
    Reward,
    Caution,
    Resize,
    Error,
}

/// <summary>
/// Small clean-room UI feedback layer for the user-testing pass. The clips are synthesized once
/// at startup, so no temporary placeholder audio assets need to ship. Buttons receive one short
/// release cue only; hover/focus changes stay silent to avoid fatiguing desktop use.
/// </summary>
public partial class UiFeedbackAudioBootstrap : Node
{
    private const int MixRate = 22_050;
    private const string HookMeta = "desktop_buddy_ui_feedback_hooked";
    private const string WorkHookMeta = "desktop_buddy_work_feedback_hooked";

    private AudioStreamPlayer _player = null!;
    private AudioStreamWav _click = null!;
    private AudioStreamWav _confirm = null!;
    private AudioStreamWav _purchase = null!;
    private AudioStreamWav _reward = null!;
    private AudioStreamWav _caution = null!;
    private AudioStreamWav _resize = null!;
    private AudioStreamWav _error = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _player = new AudioStreamPlayer
        {
            Name = "UiFeedbackPlayer",
            ProcessMode = ProcessModeEnum.Always,
            VolumeDb = -14.0f,
        };
        AddChild(_player);

        _click = Tone(0.035, 720.0, 540.0, 0.12);
        _confirm = TwoTone(0.085, 620.0, 930.0, 0.13);
        _purchase = PurchaseTone();
        _reward = RewardTone();
        _caution = Tone(0.060, 260.0, 190.0, 0.11);
        _resize = Tone(0.028, 520.0, 610.0, 0.08);
        _error = TwoTone(0.095, 260.0, 180.0, 0.12);

        GetTree().NodeAdded += OnNodeAdded;
        HookTree(GetTree().Root);
    }

    public override void _ExitTree()
    {
        if (GetTree() is SceneTree tree)
            tree.NodeAdded -= OnNodeAdded;
        if (GodotObject.IsInstanceValid(_player))
        {
            _player.Stop();
            _player.Stream = null;
        }
    }

    public static void TryPlay(Node context, UiFeedbackCue cue)
    {
        if (!GodotObject.IsInstanceValid(context))
            return;
        if (context.GetTree().Root.GetNodeOrNull<UiFeedbackAudioBootstrap>(nameof(UiFeedbackAudioBootstrap)) is { } audio)
            audio.Play(cue);
    }

    public void Play(UiFeedbackCue cue)
    {
        if (!GodotObject.IsInstanceValid(_player))
            return;
        _player.Stream = cue switch
        {
            UiFeedbackCue.Confirm => _confirm,
            UiFeedbackCue.Purchase => _purchase,
            UiFeedbackCue.Reward => _reward,
            UiFeedbackCue.Caution => _caution,
            UiFeedbackCue.Resize => _resize,
            UiFeedbackCue.Error => _error,
            _ => _click,
        };
        _player.Play();
    }

    private void OnNodeAdded(Node node) => HookNode(node);

    private void HookTree(Node node)
    {
        HookNode(node);
        foreach (Node child in node.GetChildren())
            HookTree(child);
    }

    private void HookNode(Node node)
    {
        if (node is BaseButton button)
            HookButton(button);
        else if (node is PopupMenu popup)
            HookPopup(popup);
        else if (node is WorkCompanionCoordinator work)
            HookWork(work);
    }

    private void HookButton(BaseButton button)
    {
        if (button.HasMeta(HookMeta))
            return;
        button.SetMeta(HookMeta, true);
        button.Pressed += () =>
        {
            if (!button.Disabled && button.IsVisibleInTree())
                Play(CueFor(button));
        };
    }

    private void HookPopup(PopupMenu popup)
    {
        if (popup.HasMeta(HookMeta))
            return;
        popup.SetMeta(HookMeta, true);
        popup.IdPressed += _ => Play(UiFeedbackCue.Click);
    }

    private void HookWork(WorkCompanionCoordinator work)
    {
        if (work.HasMeta(WorkHookMeta))
            return;
        work.SetMeta(WorkHookMeta, true);
        bool hadFirstEntryReward = work.Progress.FirstEntryGlassesGranted;
        work.ActiveChanged += active =>
        {
            if (active)
            {
                bool hasFirstEntryReward = work.Progress.FirstEntryGlassesGranted;
                if (!hadFirstEntryReward && hasFirstEntryReward)
                    Play(UiFeedbackCue.Reward);
                hadFirstEntryReward = hasFirstEntryReward;
                return;
            }

            // Work exit is normally a double-click directly on the companion rather than a
            // standard UI button, so it needs its own short completion cue.
            Play(UiFeedbackCue.Confirm);
        };
    }

    private static UiFeedbackCue CueFor(BaseButton button)
    {
        string name = button.Name.ToString();
        string text = button is Button visual ? visual.Text : string.Empty;
        string label = $"{name} {text}".ToLowerInvariant();

        if (ContainsAny(label, "buy", "purchase"))
            return UiFeedbackCue.Purchase;
        if (ContainsAny(label, "delete", "discard", "revert", "reset", "cancel"))
            return UiFeedbackCue.Caution;
        if (ContainsAny(label, "save", "equip", "use", "place", "done", "keep", "create", "confirm"))
            return UiFeedbackCue.Confirm;
        return UiFeedbackCue.Click;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        foreach (string needle in needles)
            if (value.Contains(needle, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static AudioStreamWav Tone(double seconds, double startHz, double endHz, double gain)
    {
        return Synthesize(seconds, (sample, progress) =>
        {
            double frequency = startHz + ((endHz - startHz) * progress);
            double envelope = Math.Sin(Math.PI * progress);
            return Math.Sin(Math.Tau * frequency * sample / MixRate) * envelope * gain;
        });
    }

    private static AudioStreamWav TwoTone(double seconds, double firstHz, double secondHz, double gain)
    {
        return Synthesize(seconds, (sample, progress) =>
        {
            double frequency = progress < 0.48 ? firstHz : secondHz;
            double envelope = Math.Sin(Math.PI * progress);
            return Math.Sin(Math.Tau * frequency * sample / MixRate) * envelope * gain;
        });
    }

    private static AudioStreamWav PurchaseTone()
    {
        return Synthesize(0.13, (sample, progress) =>
        {
            double frequency = progress < 0.34 ? 520.0 : progress < 0.68 ? 660.0 : 820.0;
            double envelope = Math.Sin(Math.PI * progress);
            return Math.Sin(Math.Tau * frequency * sample / MixRate) * envelope * 0.12;
        });
    }

    private static AudioStreamWav RewardTone()
    {
        return Synthesize(0.17, (sample, progress) =>
        {
            double frequency = progress < 0.25 ? 660.0 :
                progress < 0.50 ? 830.0 :
                progress < 0.75 ? 990.0 : 1240.0;
            double envelope = Math.Sin(Math.PI * progress);
            return Math.Sin(Math.Tau * frequency * sample / MixRate) * envelope * 0.11;
        });
    }

    private static AudioStreamWav Synthesize(double seconds, Func<int, double, double> sampleAt)
    {
        int samples = Math.Max(1, (int)Math.Round(seconds * MixRate));
        var data = new byte[samples * 2];
        for (int sample = 0; sample < samples; sample++)
        {
            double progress = sample / (double)samples;
            double normalized = Math.Clamp(sampleAt(sample, progress), -1.0, 1.0);
            short pcm = (short)Math.Round(normalized * short.MaxValue);
            data[sample * 2] = (byte)(pcm & 0xff);
            data[sample * 2 + 1] = (byte)((pcm >> 8) & 0xff);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
        };
    }
}
