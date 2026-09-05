using System;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// The outward-facing links: a Steam wishlist call to action and the community/support links,
/// both as command-bar entries, plus the itch-only welcome dialog shown once per process launch.
/// The full release drops the wishlist entry — by then there is nothing left to wishlist.
/// </summary>
public sealed partial class CommunityLinksBootstrap : Node
{
    private const string SteamStoreUrl = "https://store.steampowered.com/app/5114950/Desktop_Buddy";
    private const string YouTubeUrl = "https://www.youtube.com/@Vvoiddev";
    private const string DiscordUrl = "https://discord.gg/zymjKpcd4J";
    private const string XUrl = "https://x.com/vvoidstudios";

    private const string WishlistCommandId = "command.wishlist_steam";
    private const int WishlistCommandOrder = 900;
    private const string WishlistLabel = "Wishlist Desktop Buddy on Steam";
    private const string WishlistTooltipReady = "Open the Desktop Buddy Steam store page in your browser.";
    private const string WishlistTooltipPending = "Steam store link coming soon.";

    private const string CommunityCommandId = "command.community";
    private const int CommunityCommandOrder = 901;
    private const string CommunityLabel = "Community";
    private const string CommunityTooltip = "Discord, YouTube and X — news, help and feedback.";
    private const string CommunityText =
        "Come say hello, ask for help, or tell me what you would like to see next.";

    private const string WelcomeText =
        "Hi vvoiddev here, thank you for playing Desktop Buddy! If you like what you're playing then please support me by wishlisting the game on Steam!\n\n" +
        "If not, then just enjoy this demo to your heart's content :)";

    private IDisposable? _wishlistRegistration;
    private IDisposable? _communityRegistration;
    private bool _welcomeShown;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _welcomeShown = !DemoScope.IsItchIo;
    }

    public override void _Process(double delta)
    {
        Win98CommandBarBootstrap? commandBar = GetNodeOrNull<Win98CommandBarBootstrap>(
            "/root/Win98CommandBarBootstrap");
        if (!GodotObject.IsInstanceValid(commandBar))
            return;

        if (!DemoScope.IsFullRelease)
        {
            _wishlistRegistration ??= commandBar!.RegisterTopLevelCommand(
                new TopLevelCommandDefinition(
                    WishlistCommandId,
                    WishlistLabel,
                    HasSteamStoreUrl ? WishlistTooltipReady : WishlistTooltipPending,
                    WishlistCommandOrder),
                OpenSteamStore,
                isEnabled: () => HasSteamStoreUrl);
        }

        _communityRegistration ??= commandBar!.RegisterTopLevelCommand(
            new TopLevelCommandDefinition(
                CommunityCommandId,
                CommunityLabel,
                CommunityTooltip,
                CommunityCommandOrder),
            ShowCommunityDialog);

        if (!_welcomeShown)
            TryShowWelcome();

        bool wishlistDone = _wishlistRegistration is not null || DemoScope.IsFullRelease;
        if (wishlistDone && _communityRegistration is not null && _welcomeShown)
            SetProcess(false);
    }

    public override void _ExitTree()
    {
        _wishlistRegistration?.Dispose();
        _wishlistRegistration = null;
        _communityRegistration?.Dispose();
        _communityRegistration = null;
    }

    private static bool HasSteamStoreUrl => !string.IsNullOrWhiteSpace(SteamStoreUrl);

    private static void OpenSteamStore() => Open(SteamStoreUrl, "the Desktop Buddy Steam page");

    private static void Open(string url, string what)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        Error error = OS.ShellOpen(url);
        if (error != Error.Ok)
            GD.PushWarning($"Could not open {what}: {error}.");
    }

    /// <summary>
    /// The in-scene Win98 shell frame, which is the Control every modal in this app hangs from.
    /// Null before the shell has built itself, which is why both dialogs are polled for.
    /// </summary>
    private Win98WindowFrame? ShellFrame =>
        GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) is Win98WindowFrame frame &&
        GodotObject.IsInstanceValid(frame)
            ? frame
            : null;

    private void ShowCommunityDialog()
    {
        Win98WindowFrame? frame = ShellFrame;
        if (frame is null)
            return;

        if (frame.FindChild("CommunityLinksBlocker", false, false) is Control)
            return;

        Control blocker = Win98Dialog.Blocker(frame, "CommunityLinksBlocker");
        blocker.ZIndex = 500;

        PanelContainer dialog = Win98Dialog.Create(
            "CommunityLinksDialog",
            "Community",
            new Vector2(520, 240),
            out VBoxContainer body,
            draggable: true);
        blocker.AddChild(dialog);

        body.AddChild(new Label
        {
            Name = "CommunityLinksMessage",
            Text = CommunityText,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        var links = new HBoxContainer { Name = "CommunityLinksRow" };
        links.AddThemeConstantOverride("separation", 8);
        body.AddChild(links);
        Win98Dialog.Action(links, "Discord", () => Open(DiscordUrl, "the Discord invite")).Name =
            "CommunityDiscordButton";
        Win98Dialog.Action(links, "YouTube", () => Open(YouTubeUrl, "the YouTube channel")).Name =
            "CommunityYouTubeButton";
        Win98Dialog.Action(links, "X", () => Open(XUrl, "the X profile")).Name =
            "CommunityXButton";

        var actions = new HBoxContainer
        {
            Name = "CommunityLinksActions",
            Alignment = BoxContainer.AlignmentMode.End,
        };
        actions.AddThemeConstantOverride("separation", 8);
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Close", () =>
        {
            blocker.Visible = false;
            blocker.QueueFree();
        }).Name = "CommunityLinksCloseButton";

        blocker.Visible = true;
        dialog.Visible = true;
    }

    private void TryShowWelcome()
    {
        Win98WindowFrame? frame = ShellFrame;
        if (frame is null)
            return;

        // Win98WindowFrame is parented directly under Win98BuddyShellController, which is a
        // CanvasLayer rather than a Control. The old code required the frame's parent to be a
        // Control, so this method returned forever even though the wishlist command itself had
        // already registered successfully. The frame is the full in-scene shell and is the
        // correct Control host for a modal blocker.
        Control overlay = frame;

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
