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
/// Steam demos do not receive their own Community Hub, so their normal /app/&lt;id&gt;/workshop/
/// page is not a reliable browser. This partial builds the Workshop experience in-game instead:
/// SteamUGC supplies the items and preview URLs, while the presentation stays inside Desktop
/// Buddy's configurable Win98 shell.
/// </summary>
public partial class WorkshopPanel
{
    private const int WorkshopQueryPageSize = 50;
    private const long PreviewBodyLimitBytes = 5L * 1024 * 1024;

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

    /// <summary>Called from the panel's existing process hook after the legacy builder has run.</summary>
    private void EnsureWorkshopBrowserLayout()
    {
        if (_browserLayoutBuilt || !_built || !GodotObject.IsInstanceValid(_root))
            return;

        VBoxContainer? chrome = _root.GetNodeOrNull<VBoxContainer>("WorkshopChrome");
        if (chrome is null || chrome.GetChildCount() < 2 || chrome.GetChild(1) is not MarginContainer margin ||
            margin.GetChildCount() == 0 || margin.GetChild(0) is not VBoxContainer column || column.GetChildCount() < 10)
            return;

        // Resolve the live transport owned by the same WorkshopBootstrap. For the Demo the public
        // sharing transport is a mirroring wrapper, but discovery intentionally talks to the inner
        // runtime transport whose Workshop AppID is 5228990. The full game resolves the same node
        // with 5114950.
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
        StyleWorkshopTabs(_workshopTabs);
        column.AddChild(_workshopTabs);

        VBoxContainer browseTab = NewTabPage("BrowseContent");
        VBoxContainer uploadTab = NewTabPage("UploadContent");
        _workshopTabs.AddChild(browseTab);
        _workshopTabs.AddChild(uploadTab);
        _workshopTabs.SetTabTitle(0, "Browse Content");
        _workshopTabs.SetTabTitle(1, "Upload Content");

        BuildBrowseContent(browseTab, librarySplit);

        publishHeading.Reparent(uploadTab);
        if (publishHeading is Label publishLabel)
            publishLabel.Text = "Upload a new Workshop item";
        _title.Reparent(uploadTab);
        _description.Reparent(uploadTab);
        publishRow.Reparent(uploadTab);
        legal.Reparent(uploadTab);
        uploadTab.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // Keep transfer progress and status shared below both tabs. The old browse row remains a
        // hidden compatibility host for its button references so RefreshAvailability never touches
        // freed controls.
        column.MoveChild(_workshopTabs, Math.Min(2, column.GetChildCount() - 1));
        _browserLayoutBuilt = true;
        _workshopTabs.TabChanged += OnWorkshopTabChanged;
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

    private static void StyleWorkshopTabs(TabContainer tabs)
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

    private void BuildBrowseContent(VBoxContainer browseTab, Control librarySplit)
    {
        var innerTabs = new TabContainer
        {
            Name = "BrowseSections",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        StyleWorkshopTabs(innerTabs);
        browseTab.AddChild(innerTabs);

        VBoxContainer community = NewTabPage("Community");
        VBoxContainer library = NewTabPage("MyLibrary");
        innerTabs.AddChild(community);
        innerTabs.AddChild(library);
        innerTabs.SetTabTitle(0, "Community");
        innerTabs.SetTabTitle(1, "My Library");

        var toolbar = new HBoxContainer();
        community.AddChild(toolbar);
        toolbar.AddChild(new Label { Text = "Sort:" });
        _sortMode = new OptionButton { CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(130), 0) };
        _sortMode.AddItem("Most Popular", (int)WorkshopBrowseSort.MostPopular);
        _sortMode.AddItem("Newest", (int)WorkshopBrowseSort.MostRecent);
        _sortMode.ItemSelected += _ => _ = RefreshCommunityAsync(resetPage: true);
        toolbar.AddChild(_sortMode);

        toolbar.AddChild(new Label { Text = "Show:" });
        _contentFilter = new OptionButton { CustomMinimumSize = new Vector2(Win98ThemeFactory.Px(150), 0) };
        _contentFilter.AddItem("All Content", 0);
        _contentFilter.AddItem("Buddies", 1);
        _contentFilter.AddItem("Room Paintings", 2);
        _contentFilter.ItemSelected += _ => RebuildCommunityCards();
        toolbar.AddChild(_contentFilter);
        toolbar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var refresh = new Button { Text = "Refresh", TooltipText = "Reload this Workshop page from Steam." };
        refresh.Pressed += () => _ = RefreshCommunityAsync(resetPage: false);
        toolbar.AddChild(refresh);

        var summaryPanel = new PanelContainer();
        summaryPanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Recessed(Win98ThemeFactory.Face, 1));
        community.AddChild(summaryPanel);
        _communitySummary = new Label
        {
            Text = "Loading Workshop content...",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
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
            if (_communityPage > 1)
            {
                _communityPage--;
                _ = RefreshCommunityAsync(resetPage: false);
            }
        };
        pager.AddChild(_previousPage);
        _nextPage = new Button { Text = "Next >" };
        _nextPage.Pressed += () =>
        {
            _communityPage++;
            _ = RefreshCommunityAsync(resetPage: false);
        };
        pager.AddChild(_nextPage);

        librarySplit.Reparent(library);
        librarySplit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    }

    private void OnWorkshopTabChanged(long tab)
    {
        if (tab == 0 && _communityItems.Count == 0)
            _ = RefreshCommunityAsync(resetPage: false);
    }

    private async Task RefreshCommunityAsync(bool resetPage)
    {
        if (!_browserLayoutBuilt || !GodotObject.IsInstanceValid(_communityGrid))
            return;
        if (resetPage)
            _communityPage = 1;

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
                "Community browsing requires the live Steam build. Your subscribed/local library remains available in My Library.";
            _previousPage.Disabled = true;
            _nextPage.Disabled = true;
            return;
        }

        _communitySummary.Text = "Loading Workshop content...";
        WorkshopBrowseSort sort = (WorkshopBrowseSort)_sortMode.GetSelectedId();
        WorkshopBrowsePage result = await _discovery.BrowseAsync(sort, _communityPage, token);
        if (token.IsCancellationRequested || generation != _previewGeneration)
            return;

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
        if (!GodotObject.IsInstanceValid(_communityGrid))
            return;
        Clear(_communityGrid);

        int filter = (int)_contentFilter.GetSelectedId();
        WorkshopBrowseItem[] visible = _communityItems
            .Where(item => filter switch
            {
                1 => string.Equals(item.Item.ContentType, ShareContentTypes.BuddyCharacter, StringComparison.Ordinal),
                2 => string.Equals(item.Item.ContentType, ShareContentTypes.RoomPainting, StringComparison.Ordinal),
                _ => true,
            })
            .ToArray();

        if (visible.Length == 0)
        {
            var empty = new Label
            {
                Text = _communityItems.Count == 0
                    ? "No Workshop items were returned for this page."
                    : "No items on this page match the selected content filter.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            _communityGrid.AddChild(empty);
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

        var previewStack = new Control
        {
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(145)),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        previewFrame.AddChild(previewStack);
        previewStack.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var placeholder = new Label
        {
            Text = string.IsNullOrWhiteSpace(browse.PreviewUrl) ? "No preview image" : "Loading preview...",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        placeholder.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        previewStack.AddChild(placeholder);

        var texture = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        texture.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        previewStack.AddChild(texture);
        if (!string.IsNullOrWhiteSpace(browse.PreviewUrl))
            _ = LoadPreviewIntoAsync(browse.PreviewUrl, texture, placeholder, generation);

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
        var meta = new Label
        {
            Text = subscribed ? $"{type}  •  Subscribed" : type,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        meta.AddThemeFontSizeOverride("font_size", Win98ThemeFactory.Px(11));
        body.AddChild(meta);

        var description = new Label
        {
            Text = string.IsNullOrWhiteSpace(item.Description) ? "No description." : item.Description,
            TooltipText = item.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, Win98ThemeFactory.Px(48)),
            MaxLinesVisible = 3,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        body.AddChild(description);

        var actions = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(actions);

        var primary = new Button
        {
            Text = subscribed ? "Import" : "Subscribe",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = subscribed
                ? "Import this subscribed item as a local Desktop Buddy copy."
                : "Subscribe to this item through Steam, then you can import it.",
        };
        primary.Pressed += () =>
        {
            if (subscribed)
                RequestImport(item);
            else
                _ = SubscribeFromBrowserAsync(item);
        };
        actions.AddChild(primary);

        var open = new Button
        {
            Text = "Open Page",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TooltipText = "Open this individual Steam Workshop item page in your browser.",
        };
        open.Pressed += () => _sharing?.OpenWorkshopItem(item.PublishedFileId);
        actions.AddChild(open);

        return card;
    }

    private async Task SubscribeFromBrowserAsync(PublishedWorkshopItem item)
    {
        if (_busy || _discovery is null)
            return;

        WorkshopSubscriptionChangeResult result = default;
        await RunBusyAsync(async (_, token) =>
        {
            SetStatus($"Subscribing to '{item.DisplayName}'...");
            result = await _discovery.SubscribeAsync(item.PublishedFileId, token);
        });

        if (result.IsSuccess)
        {
            SetStatus($"Subscribed to '{item.DisplayName}'. You can import it now.");
            await RefreshSubscriptionsAsync();
            await RefreshCommunityAsync(resetPage: false);
        }
        else
        {
            SetStatus(result.Detail ?? $"Could not subscribe to '{item.DisplayName}'.");
        }
    }

    private async Task LoadPreviewIntoAsync(
        string url,
        TextureRect target,
        Label placeholder,
        int generation)
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

        var request = new HTTPRequest
        {
            Timeout = 12.0,
            BodySizeLimit = PreviewBodyLimitBytes,
            MaxRedirects = 4,
        };
        AddChild(request);
        try
        {
            Error started = request.Request(url);
            if (started != Error.Ok)
            {
                if (GodotObject.IsInstanceValid(placeholder)) placeholder.Text = "Preview unavailable";
                return;
            }

            Variant[] response = await ToSignal(request, HTTPRequest.SignalName.RequestCompleted);
            if (generation != _previewGeneration || !GodotObject.IsInstanceValid(target))
                return;

            long result = response.Length > 0 ? response[0].AsInt64() : -1;
            long responseCode = response.Length > 1 ? response[1].AsInt64() : 0;
            byte[] bytes = response.Length > 3 ? response[3].AsByteArray() : [];
            if (result != (long)HTTPRequest.Result.Success || responseCode is < 200 or >= 300 || bytes.Length == 0)
            {
                if (GodotObject.IsInstanceValid(placeholder)) placeholder.Text = "Preview unavailable";
                return;
            }

            Image image = new();
            Error decoded = image.LoadPngFromBuffer(bytes);
            if (decoded != Error.Ok) decoded = image.LoadJpgFromBuffer(bytes);
            if (decoded != Error.Ok) decoded = image.LoadWebpFromBuffer(bytes);
            if (decoded != Error.Ok)
            {
                if (GodotObject.IsInstanceValid(placeholder)) placeholder.Text = "Preview unavailable";
                return;
            }

            ImageTexture texture = ImageTexture.CreateFromImage(image);
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
