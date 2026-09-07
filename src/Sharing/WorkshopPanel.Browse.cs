using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Sharing;
using DesktopBuddy.Platform.Steam;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sharing;

/// <summary>
/// Win98-styled in-game Workshop browser. Steam demos do not have their own Community Hub, so
/// discovery has to use ISteamUGC directly instead of linking to /app/&lt;demo-id&gt;/workshop/.
/// </summary>
public partial class WorkshopPanel
{
    private const int WorkshopQueryPageSize = 50;
    private const int PreviewBodyLimitBytes = 5 * 1024 * 1024;

    private bool _browserLayoutBuilt;
    private IWorkshopDiscoveryTransport? _discovery;
    private TabContainer _workshopTabs = null!;
    private GridContainer _communityGrid = null!;
    private Label _communitySummary = null!;
    private OptionButton _sortMode = null!;
    private OptionButton _contentFilter = null!;
    private Button _previousPage = null!;
    private Button _nextPage = null!;
    private uint _communityPage = 1;
    private uint _communityTotal;
    private IReadOnlyList<WorkshopBrowseItem> _communityItems = Array.Empty<WorkshopBrowseItem>();
    private CancellationTokenSource? _browseCancellation;
    private int _previewGeneration;
    private readonly Dictionary<string, Texture2D> _previewCache = new(StringComparer.Ordinal);

    /// <summary>Rehomes the legacy controls into Browse Content / Upload Content once.</summary>
    private void EnsureWorkshopBrowserLayout()
    {
        if (_browserLayoutBuilt || !_built || !GodotObject.IsInstanceValid(_root)) return;

        VBoxContainer? chrome = _root.GetNodeOrNull<VBoxContainer>("WorkshopChrome");
        if (chrome is null || chrome.GetChildCount() < 2 ||
            chrome.GetChild(1) is not MarginContainer margin || margin.GetChildCount() == 0 ||
            margin.GetChild(0) is not VBoxContainer column || column.GetChildCount() < 10)
            return;

        // WorkshopBootstrap keeps the direct runtime transport as a sibling of the panel. In a
        // Demo that transport is scoped to 5228990; the mirroring wrapper is only used for upload.
        _discovery = GetParent()?.GetNodeOrNull<GodotSteamWorkshopTransport>(nameof(GodotSteamWorkshopTransport));

        Control oldBrowseRow = (Control)column.GetChild(1);
        Control legal = (Control)column.GetChild(2);
        Control publishHeading = (Control)column.GetChild(3);
        Control publishRow = (Control)column.GetChild(6);
        Control librarySplit = (Control)column.GetChild(7);
        oldBrowseRow.Visible = false;

        _workshopTabs = new TabContainer
        {
            Name = "WorkshopTabs",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        StyleTabs(_workshopTabs);
        column.AddChild(_workshopTabs);

        VBoxContainer browseTab = NewTabPage("BrowseContent");
        VBoxContainer uploadTab = NewTabPage("UploadContent");
        _workshopTabs.AddChild(browseTab);
        _workshopTabs.AddChild(uploadTab);
        _workshopTabs.SetTabTitle(0, "Browse Content");
        _workshopTabs.SetTabTitle(1, "Upload Content");

        BuildBrowseTab(browseTab, librarySplit);

        publishHeading.Reparent(uploadTab);
        if (publishHeading is Label heading) heading.Text = "Upload a new Workshop item";
        _title.Reparent(uploadTab);
        _description.Reparent(uploadTab);
        publishRow.Reparent(uploadTab);
        legal.Reparent(uploadTab);
        uploadTab.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        column.MoveChild(_workshopTabs, Math.Min(2, column.GetChildCount() - 1));
        _browserLayoutBuilt = true;
        _workshopTabs.TabChanged += tab =>
        {
            if (tab == 0 && _communityItems.Count == 0)
                _ = RefreshCommunityAsync(resetPage: false);
        };
        _ = RefreshCommunityAsync(resetPage: true);
    }

    private static VBoxContainer NewTabPage(string name)
    {
        var page = new VBoxContainer
        {
            Name = name,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        page.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(6));
        return page;
    }

    private static void StyleTabs(TabContainer tabs)
    {
        tabs.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        TabBar bar = tabs.GetTabBar();
        bar.AddThemeStyleboxOverride("tab_selected", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        bar.AddThemeStyleboxOverride("tab_unselected", Win98ThemeFactory.Raised(Win98ThemeFactory.Highlight, 1));
        bar.AddThemeStyleboxOverride("tab_hovered", Win98ThemeFactory.Raised(Win98ThemeFactory.Light, 1));
        bar.AddThemeStyleboxOverride("focus", Win98ThemeFactory.FocusBox());
        bar.AddThemeColorOverride("font_selected_color", Win98ThemeFactory.Dark);
        bar.AddThemeColorOverride("font_unselected_color", Win98ThemeFactory.Dark);
        bar.AddThemeColorOverride("font_hovered_color", Win98ThemeFactory.Dark);
    }

    private void BuildBrowseTab(VBoxContainer browseTab, Control librarySplit)
    {
        var sections = new TabContainer
        {
            Name = "BrowseSections",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        StyleTabs(sections);
        browseTab.AddChild(sections);

        VBoxContainer community = NewTabPage("Community");
        VBoxContainer library = NewTabPage("MyLibrary");
        sections.AddChild(community);
        sections.AddChild(library);
        sections.SetTabTitle(0, "Community");
        sections.SetTabTitle(1, "My Library");

        var toolbar = new HBoxContainer();
        community.AddChild(toolbar);
        toolbar.AddChild(new Label { Text = "Sort:" });
        _sortMode = new OptionButton { CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(130), 0) };
        _sortMode.AddItem("Most Popular", (int)WorkshopBrowseSort.MostPopular);
        _sortMode.AddItem("Newest", (int)WorkshopBrowseSort.MostRecent);
        _sortMode.ItemSelected += selected => { _ = RefreshCommunityAsync(resetPage: true); };
        toolbar.AddChild(_sortMode);

        toolbar.AddChild(new Label { Text = "Show:" });
        _contentFilter = new OptionButton { CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(150), 0) };
        _contentFilter.AddItem("All Content", 0);
        _contentFilter.AddItem("Buddies", 1);
        _contentFilter.AddItem("Room Paintings", 2);
        _contentFilter.ItemSelected += selected => RebuildCommunityCards();
        toolbar.AddChild(_contentFilter);
        toolbar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var refresh = new Button { Text = "Refresh", TooltipText = "Reload Workshop content from Steam." };
        refresh.Pressed += () => _ = RefreshCommunityAsync(resetPage: false);
        toolbar.AddChild(refresh);

        var summaryPanel = new PanelContainer();
        summaryPanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        community.AddChild(summaryPanel);
        _communitySummary = new Label { Text = "Loading Workshop content...", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        summaryPanel.AddChild(_communitySummary);

        var scroll = new ScrollContainer
        {
            Name = "CommunityWorkshopScroll",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        community.AddChild(scroll);
        _communityGrid = new GridContainer
        {
            Name = "CommunityWorkshopGrid",
            Columns = 3,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _communityGrid.AddThemeConstantOverride("h_separation", Win98ThemeFactory.Px(8));
        _communityGrid.AddThemeConstantOverride("v_separation", Win98ThemeFactory.Px(8));
        scroll.AddChild(_communityGrid);

        var pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        community.AddChild(pager);
        _previousPage = new Button { Text = "< Previous" };
        _previousPage.Pressed += () =>
        {
            if (_communityPage <= 1) return;
            _communityPage--;
            _ = RefreshCommunityAsync(resetPage: false);
        };
        pager.AddChild(_previousPage);
        _nextPage = new Button { Text = "Next >" };
        _nextPage.Pressed += () =>
        {
            _communityPage++;
            _ = RefreshCommunityAsync(resetPage: false);
        };
        pager.AddChild(_nextPage);

        // Existing subscribed items + imported room paintings become the personal-library view.
        librarySplit.Reparent(library);
        librarySplit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    }

    private async Task RefreshCommunityAsync(bool resetPage)
    {
        if (!_browserLayoutBuilt || !GodotObject.IsInstanceValid(_communityGrid)) return;
        if (resetPage) _communityPage = 1;

        _browseCancellation?.Cancel();
        _browseCancellation?.Dispose();
        _browseCancellation = new CancellationTokenSource();
        CancellationToken token = _browseCancellation.Token;
        int generation = ++_previewGeneration;

        if (_discovery is null || !_discovery.IsDiscoveryAvailable)
        {
            _communityItems = Array.Empty<WorkshopBrowseItem>();
            Clear(_communityGrid);
            _communitySummary.Text = _discovery?.DiscoveryUnavailableReason ??
                "Community browsing requires the live Steam build. My Library remains available.";
            _previousPage.Disabled = true;
            _nextPage.Disabled = true;
            return;
        }

        _communitySummary.Text = "Loading Workshop content...";
        WorkshopBrowseSort sort = (WorkshopBrowseSort)_sortMode.GetSelectedId();
        WorkshopBrowsePage result = await _discovery.BrowseAsync(sort, _communityPage, token);
        if (token.IsCancellationRequested || generation != _previewGeneration) return;

        if (!result.IsSuccess)
        {
            _communityItems = Array.Empty<WorkshopBrowseItem>();
            Clear(_communityGrid);
            _communitySummary.Text = result.Detail ?? "Steam could not load Workshop content.";
            _previousPage.Disabled = _communityPage <= 1;
            _nextPage.Disabled = true;
            return;
        }

        _communityItems = result.Items;
        _communityTotal = result.TotalMatching;
        RebuildCommunityCards();
    }

    private void RebuildCommunityCards()
    {
        if (!GodotObject.IsInstanceValid(_communityGrid)) return;
        Clear(_communityGrid);

        int filter = (int)_contentFilter.GetSelectedId();
        WorkshopBrowseItem[] visible = _communityItems.Where(item => filter switch
        {
            1 => string.Equals(item.Item.ContentType, ShareContentTypes.BuddyCharacter, StringComparison.Ordinal),
            2 => string.Equals(item.Item.ContentType, ShareContentTypes.RoomPainting, StringComparison.Ordinal),
            _ => true,
        }).ToArray();

        if (visible.Length == 0)
        {
            _communityGrid.AddChild(new Label
            {
                Text = _communityItems.Count == 0
                    ? "No Workshop items were returned for this page."
                    : "No items on this page match the selected filter.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }
        else
        {
            foreach (WorkshopBrowseItem item in visible)
                _communityGrid.AddChild(BuildCommunityCard(item, _previewGeneration));
        }

        uint first = _communityItems.Count == 0 ? 0 : ((_communityPage - 1) * WorkshopQueryPageSize) + 1;
        uint last = _communityItems.Count == 0 ? 0 : first + (uint)_communityItems.Count - 1;
        _communitySummary.Text = _communityTotal > 0
            ? $"Showing {first}-{Math.Min(last, _communityTotal)} of {_communityTotal} Workshop items."
            : $"Showing {_communityItems.Count} Workshop item{(_communityItems.Count == 1 ? string.Empty : "s")} on page {_communityPage}.";
        _previousPage.Disabled = _communityPage <= 1;
        _nextPage.Disabled = _communityItems.Count == 0 ||
            (_communityTotal > 0 && _communityPage * WorkshopQueryPageSize >= _communityTotal);
    }

    private Control BuildCommunityCard(WorkshopBrowseItem browse, int generation)
    {
        PublishedWorkshopItem item = browse.Item;
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(245), Win98ThemeFactory.Px(300)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
        card.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", Win98ThemeFactory.Px(5));
        card.AddChild(body);

        var previewFrame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(145)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        previewFrame.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Dark, 1));
        body.AddChild(previewFrame);
        var stack = new Control { CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(145)) };
        previewFrame.AddChild(stack);
        stack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var placeholder = new Label
        {
            Text = string.IsNullOrWhiteSpace(browse.PreviewUrl) ? "No preview image" : "Loading preview...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        placeholder.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        stack.AddChild(placeholder);
        var image = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        stack.AddChild(image);
        if (!string.IsNullOrWhiteSpace(browse.PreviewUrl))
            _ = LoadPreviewIntoAsync(browse.PreviewUrl, image, placeholder, generation);

        var title = new Label
        {
            Text = item.DisplayName,
            TooltipText = item.DisplayName,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        title.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(15));
        body.AddChild(title);

        bool subscribed = item.State.HasFlag(WorkshopItemState.Subscribed);
        string type = item.ContentType switch
        {
            ShareContentTypes.BuddyCharacter => "Buddy",
            ShareContentTypes.RoomPainting => "Room Painting",
            _ => "Desktop Buddy Item",
        };
        var meta = new Label { Text = subscribed ? $"{type}  •  Subscribed" : type };
        meta.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(11));
        body.AddChild(meta);

        body.AddChild(new Label
        {
            Text = string.IsNullOrWhiteSpace(item.Description) ? "No description." : item.Description,
            TooltipText = item.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(48)),
            MaxLinesVisible = 3,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        });

        var actions = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(actions);
        var primary = new Button
        {
            Text = subscribed ? "Import" : "Subscribe",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = subscribed ? "Import this subscribed item." : "Subscribe through Steam.",
        };
        primary.Pressed += () =>
        {
            if (subscribed) RequestImport(item);
            else _ = SubscribeFromBrowserAsync(item);
        };
        actions.AddChild(primary);
        var open = new Button
        {
            Text = "Open Page",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Open this individual Steam Workshop item page.",
        };
        open.Pressed += () => _sharing?.OpenWorkshopItem(item.PublishedFileId);
        actions.AddChild(open);
        return card;
    }

    private async Task SubscribeFromBrowserAsync(PublishedWorkshopItem item)
    {
        if (_busy || _discovery is null) return;
        WorkshopSubscriptionChangeResult result = default;
        await RunBusyAsync(async (progress, token) =>
        {
            SetStatus($"Subscribing to '{item.DisplayName}'...");
            result = await _discovery.SubscribeAsync(item.PublishedFileId, token);
        });

        if (!result.IsSuccess)
        {
            SetStatus(result.Detail ?? $"Could not subscribe to '{item.DisplayName}'.");
            return;
        }

        SetStatus($"Subscribed to '{item.DisplayName}'. You can import it now.");
        await RefreshSubscriptionsAsync();
        await RefreshCommunityAsync(resetPage: false);
    }

    private async Task LoadPreviewIntoAsync(string url, TextureRect target, Label placeholder, int generation)
    {
        if (_previewCache.TryGetValue(url, out Texture2D? cached) && GodotObject.IsInstanceValid(cached))
        {
            if (GodotObject.IsInstanceValid(target))
            {
                target.Texture = cached;
                if (GodotObject.IsInstanceValid(placeholder)) placeholder.Visible = false;
            }
            return;
        }

        var request = new HttpRequest
        {
            Timeout = 12.0,
            BodySizeLimit = PreviewBodyLimitBytes,
            MaxRedirects = 4,
        };
        AddChild(request);
        try
        {
            if (request.Request(url) != Error.Ok)
            {
                if (GodotObject.IsInstanceValid(placeholder)) placeholder.Text = "Preview unavailable";
                return;
            }

            Variant[] response = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
            if (generation != _previewGeneration || !GodotObject.IsInstanceValid(target)) return;
            long result = response.Length > 0 ? response[0].AsInt64() : -1;
            long code = response.Length > 1 ? response[1].AsInt64() : 0;
            byte[] bytes = response.Length > 3 ? response[3].AsByteArray() : [];
            if (result != (long)HttpRequest.Result.Success || code is < 200 or >= 300 || bytes.Length == 0)
            {
                if (GodotObject.IsInstanceValid(placeholder)) placeholder.Text = "Preview unavailable";
                return;
            }

            using Image decoded = new();
            Error error = decoded.LoadPngFromBuffer(bytes);
            if (error != Error.Ok) error = decoded.LoadJpgFromBuffer(bytes);
            if (error != Error.Ok) error = decoded.LoadWebpFromBuffer(bytes);
            if (error != Error.Ok)
            {
                if (GodotObject.IsInstanceValid(placeholder)) placeholder.Text = "Preview unavailable";
                return;
            }

            ImageTexture texture = ImageTexture.CreateFromImage(decoded);
            _previewCache[url] = texture;
            target.Texture = texture;
            if (GodotObject.IsInstanceValid(placeholder)) placeholder.Visible = false;
        }
        catch
        {
            if (GodotObject.IsInstanceValid(placeholder)) placeholder.Text = "Preview unavailable";
        }
        finally
        {
            if (GodotObject.IsInstanceValid(request)) request.QueueFree();
        }
    }
}
