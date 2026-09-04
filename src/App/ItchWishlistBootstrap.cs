using System;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Itch-demo-only Steam wishlist affordances: a persistent command-bar button plus a Win98
/// welcome dialog shown once per process launch. The Steam URL intentionally remains empty until
/// the store page is ready; setting <see cref="SteamStoreUrl"/> activates both wishlist buttons.
/// </summary>
public sealed partial class ItchWishlistBootstrap : Node
{
    private const string SteamStoreUrl = "";
    private const string WishlistCommandId = "command.wishlist_steam";
    private const int WishlistCommandOrder = 900;
    private const string WishlistLabel = "Wishlist Desktop Buddy on Steam";
    private const string WishlistTooltipReady = "Open the Desktop Buddy Steam store page in your browser.";
    private const string WishlistTooltipPending = "Steam store link coming soon.";
    private const string WelcomeText =
        "Hi vvoiddev here, thank you for playing Desktop Buddy! If you like what you're playing then please support me by wishlisting the game on Steam!\n\n" +
        "If not, then just enjoy this demo to your heart's content :)";

    private IDisposable? _wishlistRegistration;
    private bool _welcomeShown;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!DemoScope.IsItchIo)
            SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!DemoScope.IsItchIo)
            return;

        Win98CommandBarBootstrap? commandBar = GetNodeOrNull<Win98CommandBarBootstrap>(
            "/root/Win98CommandBarBootstrap");
        if (!GodotObject.IsInstanceValid(commandBar))
            return;

        _wishlistRegistration ??= commandBar!.RegisterTopLevelCommand(
            new TopLevelCommandDefinition(
                WishlistCommandId,
                WishlistLabel,
                HasSteamStoreUrl ? WishlistTooltipReady : WishlistTooltipPending,
                WishlistCommandOrder),
            OpenSteamStore,
            isEnabled: () => HasSteamStoreUrl);

        if (!_welcomeShown)
            TryShowWelcome();

        if (_wishlistRegistration is not null && _welcomeShown)
            SetProcess(false);
    }

    public override void _ExitTree()
    {
        _wishlistRegistration?.Dispose();
        _wishlistRegistration = null;
    }

    private static bool HasSteamStoreUrl => !string.IsNullOrWhiteSpace(SteamStoreUrl);

    private static void OpenSteamStore()
    {
        if (!HasSteamStoreUrl)
            return;

        Error error = OS.ShellOpen(SteamStoreUrl);
        if (error != Error.Ok)
            GD.PushWarning($"Could not open the Desktop Buddy Steam page: {error}.");
    }

    private void TryShowWelcome()
    {
        Win98WindowFrame? frame = GetTree().Root.FindChild(
            nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
        if (!GodotObject.IsInstanceValid(frame))
            return;

        // Win98WindowFrame is parented directly under Win98BuddyShellController, which is a
        // CanvasLayer rather than a Control. The old code required the frame's parent to be a
        // Control, so this method returned forever even though the wishlist command itself had
        // already registered successfully. The frame is the full in-scene shell and is the
        // correct Control host for a modal blocker.
        Control overlay = frame!;

        if (overlay.FindChild("ItchWishlistWelcomeBlocker", false, false) is Control)
        {
            _welcomeShown = true;
            return;
        }

        Control blocker = Win98Dialog.Blocker(overlay, "ItchWishlistWelcomeBlocker");
        blocker.ZIndex = 500;

        PanelContainer dialog = Win98Dialog.Create(
            "ItchWishlistWelcomeDialog",
            "Welcome to Desktop Buddy!",
            new Vector2(560, 260),
            out VBoxContainer body,
            draggable: true);
        blocker.AddChild(dialog);

        var message = new Label
        {
            Name = "ItchWishlistWelcomeMessage",
            Text = WelcomeText,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddChild(message);

        var actions = new HBoxContainer
        {
            Name = "ItchWishlistWelcomeActions",
            Alignment = BoxContainer.AlignmentMode.End,
        };
        actions.AddThemeConstantOverride("separation", 8);
        body.AddChild(actions);

        Button wishlist = Win98Dialog.Action(actions, "Wishlist on Steam", OpenSteamStore);
        wishlist.Name = "ItchWishlistWelcomeSteamButton";
        wishlist.Disabled = !HasSteamStoreUrl;
        wishlist.TooltipText = HasSteamStoreUrl ? WishlistTooltipReady : WishlistTooltipPending;

        Button continueButton = Win98Dialog.Action(actions, "Continue", () =>
        {
            blocker.Visible = false;
            blocker.QueueFree();
        });
        continueButton.Name = "ItchWishlistWelcomeContinueButton";

        blocker.Visible = true;
        dialog.Visible = true;
        _welcomeShown = true;
        GD.Print("DESKTOP_BUDDY_ITCH_WELCOME_SHOWN");
    }
}
