using System;
using DesktopBuddy.Platform;
using DesktopBuddy.Ui;
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

    /// <summary>Any ordinary button or menu item inside a panel: ClickWithinMenus.mp3.</summary>
    MenuClick,

    /// <summary>Close boxes, menu-dismissing buttons, and the horizontal command strip.</summary>
    MenuExit,

    /// <summary>Plays nothing: another owner already sounds this action.</summary>
    None,
}

/// <summary>
/// What a control sounds like, declared by the control itself with
/// <see cref="UiFeedbackAudioBootstrap.Tag"/>. A tag beats the label guess, so a translated
/// button keeps its sound: "Close" becoming "Cerrar" changes nothing.
/// </summary>
public static class UiSfx
{
    public const string CueMeta = "sfx_cue";
    public const string LayerMeta = "sfx_layer";

    // Cue values — which clip replaces the ordinary click.
    public const string Click = "click";
    public const string Exit = "exit";

    /// <summary>
    /// This control makes no sound of its own because something else already speaks for the
    /// action — the Work Mode X, whose exit cue belongs to the coordinator so that leaving by
    /// double-click sounds identical.
    /// </summary>
    public const string Silent = "silent";

    // Layer values — which clip plays over the top of it, if any.
    public const string Money = "money";
    public const string Equip = "equip";
    public const string Confirm = "confirm";
    public const string NoLayer = "none";
}

/// <summary>
/// Small clean-room UI feedback layer for the user-testing pass. The clips are synthesized once
/// at startup, so no temporary placeholder audio assets need to ship. Buttons receive one short
/// release cue only; hover/focus changes stay silent to avoid fatiguing desktop use.
/// </summary>
public partial class UiFeedbackAudioBootstrap : Node
{
    private const int MixRate = 22_050;
    /// <summary>Scale, not a range: each press lands between 1/1.04 and 1.04 — about ±4%.</summary>
    private const float UiPitchScale = 1.04f;
    private const float PurchasePitchScale = 1.015f;
    private const int VoiceCount = 8;
    private const int LayerVoiceCount = 4;

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
    private AudioStream? _menuClick;
    private AudioStream? _menuExit;
    /// <summary>Sounds on top of the ordinary click, on its own player, not instead of it.</summary>
    private AudioStreamPlayer _layerPlayer = null!;
    private AudioStream? _confirmLayer;
    private AudioStream? _equipLayer;
    private AudioStream? _purchaseLayer;
    private AudioStream? _sliderTick;
    private AudioStreamPlayer[] _voices = Array.Empty<AudioStreamPlayer>();
    private AudioStreamPlayer[] _layerVoices = Array.Empty<AudioStreamPlayer>();
    private int _nextVoiceIndex;
    private int _nextLayerVoiceIndex;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _player = new AudioStreamPlayer
        {
            Name = "UiFeedbackPlayer",
            ProcessMode = ProcessModeEnum.Always,
            Bus = AudioMix.Ui,
            VolumeDb = -14.0f,
            MaxPolyphony = 1,
        };
        AddChild(_player);
        _layerPlayer = new AudioStreamPlayer
        {
            Name = "UiFeedbackLayerPlayer",
            ProcessMode = ProcessModeEnum.Always,
            Bus = AudioMix.Ui,
            VolumeDb = -14.0f,
            MaxPolyphony = 1,
        };
        AddChild(_layerPlayer);
        _voices = CreateVoicePool(_player, VoiceCount);
        _layerVoices = CreateVoicePool(_layerPlayer, LayerVoiceCount);

        _click = Tone(0.035, 720.0, 540.0, 0.12);
        _confirm = TwoTone(0.085, 620.0, 930.0, 0.13);
        _purchase = PurchaseTone();
        _reward = RewardTone();
        _caution = Tone(0.060, 260.0, 190.0, 0.11);
        _resize = Tone(0.028, 520.0, 610.0, 0.08);
        _error = TwoTone(0.095, 260.0, 180.0, 0.12);
        _menuClick = LoadUiClip("ClickWithinMenus.mp3");
        _menuExit = LoadUiClip("ExitClick_HorizontalMenuClick.mp3");
        _confirmLayer = LoadUiClip("Confirmation_equip_Save.mp3");
        _equipLayer = LoadUiClip("inventory_equip.mp3");
        // The money layer is longer and more tonal than the other UI clips, so the shared
        // wobble reads as an out-of-tune note rather than variation (owner feedback 2026-08-19).
        _purchaseLayer = LoadUiClip("Money_purchase.mp3", PurchasePitchScale);
        _sliderTick = LoadUiVariations("slider_tick", 6);

        GetTree().NodeAdded += OnNodeAdded;
        HookTree(GetTree().Root);
    }

    public override void _ExitTree()
    {
        if (GetTree() is SceneTree tree)
            tree.NodeAdded -= OnNodeAdded;
        if (GodotObject.IsInstanceValid(_player))
            StopVoicePool(_voices);
        if (GodotObject.IsInstanceValid(_layerPlayer))
            StopVoicePool(_layerVoices);
    }

    /// <summary>
    /// Declares what a control sounds like. Prefer this over relying on the label guess for
    /// anything shared or translatable — it is read before the label and never goes stale.
    /// Pass null to leave that half alone.
    /// </summary>
    public static void Tag(Node control, string? cue = null, string? layer = null)
    {
        if (!GodotObject.IsInstanceValid(control))
            return;
        if (cue is not null)
            control.SetMeta(UiSfx.CueMeta, cue);
        if (layer is not null)
            control.SetMeta(UiSfx.LayerMeta, layer);
    }

    /// <summary>
    /// Sounds a commitment layer from the code that performed the action rather than from the
    /// control that triggered it. Buying in Buddy Studio happens on a Buy press *and* on a
    /// double-clicked catalogue tile, which is not a button press at all — one call at the
    /// purchase itself covers both.
    /// </summary>
    public static void TryPlayLayer(Node context, string layer)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return;
        if (context.GetTree().Root.GetNodeOrNull<UiFeedbackAudioBootstrap>(nameof(UiFeedbackAudioBootstrap)) is { } audio)
        {
            audio.PlayLayer(layer switch
            {
                UiSfx.Money => audio._purchaseLayer,
                UiSfx.Equip => audio._equipLayer,
                UiSfx.Confirm => audio._confirmLayer,
                _ => null,
            });
        }
    }

    public static void TryPlay(Node context, UiFeedbackCue cue)
    {
        if (!GodotObject.IsInstanceValid(context))
            return;
        if (context.GetTree().Root.GetNodeOrNull<UiFeedbackAudioBootstrap>(nameof(UiFeedbackAudioBootstrap)) is { } audio)
            audio.Play(cue);
    }

    public static void TryPlaySliderTick(Node context)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return;
        if (context.GetTree().Root.GetNodeOrNull<UiFeedbackAudioBootstrap>(nameof(UiFeedbackAudioBootstrap)) is { } audio)
            audio.PlaySliderTick();
    }

    public void Play(UiFeedbackCue cue)
    {
        if (cue == UiFeedbackCue.None || !GodotObject.IsInstanceValid(_player))
            return;
        AudioStream stream = cue switch
        {
            UiFeedbackCue.MenuClick => _menuClick ?? (AudioStream)_click,
            UiFeedbackCue.MenuExit => _menuExit ?? (AudioStream)_caution,
            UiFeedbackCue.Confirm => _confirm,
            UiFeedbackCue.Purchase => _purchase,
            UiFeedbackCue.Reward => _reward,
            UiFeedbackCue.Caution => _caution,
            UiFeedbackCue.Resize => _resize,
            UiFeedbackCue.Error => _error,
            _ => _click,
        };

        PlayOnPool(_voices, ref _nextVoiceIndex, stream);
    }

    private void PlaySliderTick()
    {
        if (_sliderTick is not { } stream || !GodotObject.IsInstanceValid(_player))
            return;
        PlayOnPool(_voices, ref _nextVoiceIndex, stream);
    }

    /// <summary>Sounds a clip over the top of whatever click just played.</summary>
    private void PlayLayer(AudioStream? stream)
    {
        if (stream is null || !GodotObject.IsInstanceValid(_layerPlayer))
            return;
        PlayOnPool(_layerVoices, ref _nextLayerVoiceIndex, stream);
    }

    private AudioStreamPlayer[] CreateVoicePool(AudioStreamPlayer first, int count)
    {
        var voices = new AudioStreamPlayer[count];
        voices[0] = first;
        for (int index = 1; index < voices.Length; index++)
        {
            var voice = new AudioStreamPlayer
            {
                Name = $"{first.Name}Voice{index + 1}",
                ProcessMode = first.ProcessMode,
                Bus = first.Bus,
                VolumeDb = first.VolumeDb,
                MaxPolyphony = 1,
            };
            AddChild(voice);
            voices[index] = voice;
        }

        return voices;
    }

    private static void StopVoicePool(AudioStreamPlayer[] voices)
    {
        foreach (AudioStreamPlayer voice in voices)
        {
            if (!GodotObject.IsInstanceValid(voice))
                continue;
            voice.Stop();
            voice.Stream = null;
        }
    }

    private static void PlayOnPool(
        AudioStreamPlayer[] voices,
        ref int nextVoiceIndex,
        AudioStream stream)
    {
        if (voices.Length == 0)
            return;

        for (int offset = 0; offset < voices.Length; offset++)
        {
            int index = (nextVoiceIndex + offset) % voices.Length;
            AudioStreamPlayer voice = voices[index];
            if (!GodotObject.IsInstanceValid(voice) || voice.Playing)
                continue;

            voice.Stream = stream;
            voice.Play();
            nextVoiceIndex = (index + 1) % voices.Length;
            return;
        }
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
        else if (node is ItemList list)
            HookItemList(list);
        else if (node is WorkCompanionCoordinator work)
            HookWork(work);
    }

    private void HookButton(BaseButton button)
    {
        if (button.HasMeta(HookMeta))
            return;
        button.SetMeta(HookMeta, true);

        // Hook-time values are the last-resort fallback: a button that closes or rebuilds its
        // own panel is already freed by the time the press handler runs.
        UiFeedbackCue hookedCue = CueFor(button);
        AudioStream? hookedLayer = LayerFor(button);

        // Read on the way down, played on the way up. The button's own handler runs first —
        // it was connected at construction, this hook only on tree entry — and it routinely
        // rewrites the very label the sound is chosen from: buying in the shop flips "Buy" to
        // "Equip" before Pressed reaches here, which made every purchase sound like an equip.
        UiFeedbackCue armedCue = hookedCue;
        AudioStream? armedLayer = hookedLayer;
        bool armed = false;
        button.ButtonDown += () =>
        {
            if (!GodotObject.IsInstanceValid(button))
                return;
            armedCue = CueFor(button);
            armedLayer = LayerFor(button);
            armed = true;
        };
        button.Pressed += () =>
        {
            bool alive = !armed && GodotObject.IsInstanceValid(button);
            Play(armed ? armedCue : alive ? CueFor(button) : hookedCue);
            PlayLayer(armed ? armedLayer : alive ? LayerFor(button) : hookedLayer);
            armed = false;
        };
    }

    private void HookPopup(PopupMenu popup)
    {
        if (popup.HasMeta(HookMeta))
            return;
        popup.SetMeta(HookMeta, true);
        popup.IdPressed += _ => Play(UiFeedbackCue.MenuClick);
    }

    /// <summary>
    /// Rows in an ItemList — the character library and the paint layer list — are not buttons,
    /// so they need their own hook to click like the rest of the menu.
    /// </summary>
    private void HookItemList(ItemList list)
    {
        if (list.HasMeta(HookMeta))
            return;
        list.SetMeta(HookMeta, true);
        list.ItemSelected += _ => Play(UiFeedbackCue.MenuClick);
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

            // Leaving Work Mode sounds the same however it was done — the corner X or a
            // double-click on the companion — so the coordinator owns the cue and the X button
            // itself is tagged silent. Without that the X played the exit clip and this
            // handler immediately cut it off with a different sound.
            Play(UiFeedbackCue.MenuExit);
        };
    }

    private static UiFeedbackCue CueFor(BaseButton button)
    {
        // A declared tag wins over the label guess below — that guess reads user-visible text
        // and therefore breaks the moment the UI is translated.
        if (button.GetMeta(UiSfx.CueMeta, string.Empty).AsString() is { Length: > 0 } tagged)
        {
            return tagged switch
            {
                UiSfx.Exit => UiFeedbackCue.MenuExit,
                UiSfx.Silent => UiFeedbackCue.None,
                _ => UiFeedbackCue.MenuClick,
            };
        }

        string name = button.Name.ToString();
        string text = (button is Button visual ? visual.Text : string.Empty).Trim();
        string label = $"{name} {text}".ToLowerInvariant();

        // Close boxes carry no word, just a glyph, so they are matched on the exact text.
        if (text is "×" or "X" or "x" || ContainsAny(label, "close", "cancel", "exit", "dismiss"))
            return UiFeedbackCue.MenuExit;
        if (InHorizontalMenu(button))
            return UiFeedbackCue.MenuExit;
        return UiFeedbackCue.MenuClick;
    }

    /// <summary>
    /// The clip layered over a commitment press, or none. Buy and Equip live on the same
    /// button in the shop and Buddy Studio — its label changes with ownership — so panels that
    /// know which one it currently is should say so with <see cref="Tag"/>.
    /// </summary>
    private AudioStream? LayerFor(BaseButton button)
    {
        if (button.GetMeta(UiSfx.LayerMeta, string.Empty).AsString() is { Length: > 0 } tagged)
        {
            return tagged switch
            {
                UiSfx.Money => _purchaseLayer,
                UiSfx.Equip => _equipLayer,
                UiSfx.Confirm => _confirmLayer,
                _ => null,
            };
        }

        // The visible label decides, with the node name only as a fallback: node names like
        // "BuddyStudioUnsavedDiscard" contain "save" while the button means the opposite.
        string text = button is Button visual ? visual.Text.Trim() : string.Empty;
        string label = (text.Length > 0 ? text : button.Name.ToString()).ToLowerInvariant();

        if (ContainsAny(label, "buy", "purchase"))
            return _purchaseLayer;
        // "Select" is the Tools panel's equip button — the only button in the game with that
        // label, so it can be matched by word without catching anything else.
        if (ContainsAny(label, "equip", "select"))
            return _equipLayer;
        if (ContainsAny(label, "save", "confirm"))
            return _confirmLayer;
        return null;
    }

    /// <summary>
    /// True for the Win98 command strip (Shop/Tools/Settings/Paint/Work) and the desktop
    /// toolbar — the two horizontal bars whose buttons open and close menus rather than act
    /// inside one.
    /// </summary>
    private static bool InHorizontalMenu(Node node)
    {
        for (Node? ancestor = node.GetParent(); ancestor is not null; ancestor = ancestor.GetParent())
        {
            if (ancestor is DesktopToolbarWindow || ancestor.Name == "CommandRow")
                return true;
            if (ancestor is Window)
                break;
        }
        return false;
    }

    /// <summary>
    /// Loads a dropped-in UI clip, wrapped in an <see cref="AudioStreamRandomizer"/> so repeated
    /// presses are not bit-identical. The pitch scale is deliberately tiny — a UI click that
    /// audibly wobbles reads as broken, not alive.
    /// </summary>
    private static AudioStream? LoadUiClip(string fileName, float pitchScale = UiPitchScale)
    {
        if (LoadStream(fileName) is not { } stream)
            return null;

        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitch = pitchScale,
            RandomVolumeOffsetDb = 0.6f,
        };
        randomizer.AddStream(-1, stream);
        return randomizer;
    }

    private static AudioStream? LoadUiVariations(string prefix, int count)
    {
        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitch = 1.06f,
            RandomVolumeOffsetDb = 0.8f,
        };
        int loaded = 0;
        for (int index = 1; index <= count; index++)
        {
            if (LoadStream($"{prefix}{index}.mp3") is not { } stream)
                continue;
            randomizer.AddStream(index - 1, stream);
            loaded++;
        }

        return loaded == 0 ? null : randomizer;
    }

    /// <summary>
    /// The direct file load is the fallback for a build whose import metadata has not been
    /// generated yet, so a freshly dropped .mp3 plays without a reimport.
    /// </summary>
    private static AudioStream? LoadStream(string fileName)
    {
        string resourcePath = $"res://assets/sfx/ui/{fileName}";
        if (ResourceLoader.Exists(resourcePath) &&
            ResourceLoader.Load<AudioStream>(resourcePath) is { } imported)
        {
            return imported;
        }

        // The .import sidecar can exist while the imported cache does not — a fresh clone, a
        // new worktree, or a wiped .godot. ResourceLoader then reports the resource as
        // present and hands back null, which would silently mute the whole UI.
        string absolute = ProjectSettings.GlobalizePath(resourcePath);
        return FileAccess.FileExists(absolute) ? AudioStreamMP3.LoadFromFile(absolute) : null;
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
