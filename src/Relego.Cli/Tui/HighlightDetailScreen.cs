using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Relego.Cli.Infrastructure;
using Relego.Cli.Tui.ViewModels;
using Relego.Core.Contracts;

namespace Relego.Cli.Tui;

public sealed class HighlightDetailScreen : IScreen
{
    private const int DetailPageSize = 200;
    private const int DefaultTableWidth = 80;
    private const int TableHorizontalPadding = 2;
    private const int DefaultWeight = 3;
    private const int MinimumWeight = 1;
    private const int MaximumWeight = 5;
    private const int WeightColumnWidth = 8;
    private const int DotColumnWidth = 1;
    private const int MinimumHighlightColumnWidth = 18;
    private const int WeightEditorWidth = 52;
    private const int WeightEditorHeight = 7;
    private const int PreviewPopupWidth = 68;
    private const int PreviewPopupHeight = 14;
    private const int PreviewContentWidth = PreviewPopupWidth - 6;
    private readonly RelegoHttpClient _client;
    private readonly List<HighlightViewModel> _highlights;
    private readonly int _bookId;
    private readonly int _authorId;
    private readonly string _bookTitle;
    private readonly string _authorName;
    private bool _isBookExcluded;
    private bool _isAuthorExcluded;
    private string? _statusMessage;
    private ObservableCollection<string>? _highlightRows;
    private ShortcutListView? _highlightList;
    private Label? _titleLabel;
    private Label? _authorLabel;
    private Label? _summaryCountLabel;
    private Label? _summaryBookLabel;
    private Label? _summaryAuthorLabel;
    private Label? _headerLabel;
    private Label? _headerRuleLabel;
    private Label? _statusLabel;
    private FrameView? _actionMenuFrame;
    private ObservableCollection<string>? _actionRows;
    private ListView? _actionList;
    private FrameView? _weightEditorFrame;
    private Label? _weightScaleLabel;
    private Label? _weightHelpLabel;
    private FrameView? _deleteConfirmationFrame;
    private FrameView? _highlightPreviewFrame;
    private Label? _previewLabel;
    private Action<ScreenResult>? _navigate;
    private string? _previewText;
    private bool _viewCreated;
    private bool _updatingViewState;
    private TableLayout _tableLayout = CalculateTableLayout(DefaultTableWidth);

    private readonly record struct TableLayout(int HighlightWidth, int WeightWidth);

    public HighlightDetailScreen(BookViewModel book, RelegoHttpClient client)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(client);

        Book = book;
        _client = client;
        _bookId = book.BookId;
        _authorId = book.AuthorId;
        _bookTitle = book.Title;
        _authorName = book.Author;
        _isBookExcluded = book.IsBookExcluded;
        _isAuthorExcluded = book.IsAuthorExcluded;
        _highlights = [.. book.Highlights];
    }

    public BookViewModel Book { get; }

    public int SelectedIndex { get; private set; }

    public bool ActionMenuOpen { get; private set; }

    public int ActionMenuIndex { get; private set; }

    public bool WeightEditorOpen { get; private set; }

    public int PendingWeight { get; private set; } = DefaultWeight;

    public bool DeleteConfirmationOpen { get; private set; }

    public bool HighlightPreviewOpen { get; private set; }

    public string? PreviewText => _previewText;

    public string? StatusMessage => _statusMessage;

    public IReadOnlyList<HighlightViewModel> Highlights => _highlights;

    public string Title => string.Empty;

    public IReadOnlyList<(string Key, string Label)> KeyHints =>
    [
        ("↑↓", "Navigate"),
        ("Enter", "Show"),
        ("A", "Actions"),
        ("R", "Refresh"),
        ("Esc", "Go Back"),
        ("Q", "Quit")
    ];

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the parent container hierarchy")]
    public View CreateView(Action<ScreenResult> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);

        _navigate = navigate;

        var container = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };

        _titleLabel = new Label
        {
            X = TableHorizontalPadding,
            Y = 0,
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Height = 1,
            CanFocus = false,
            Text = _bookTitle
        };
        _titleLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.AccentText, TuiTheme.Palette.Background)));

        _authorLabel = new Label
        {
            X = TableHorizontalPadding,
            Y = 1,
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Height = 1,
            CanFocus = false,
            Text = $"by {_authorName}"
        };
        _authorLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.TextMuted, TuiTheme.Palette.Background)));

        _summaryCountLabel = new Label
        {
            X = TableHorizontalPadding,
            Y = 2,
            Width = Dim.Auto(),
            Height = 1,
            CanFocus = false
        };
        _summaryCountLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.TextMuted, TuiTheme.Palette.Background)));

        _summaryBookLabel = new Label
        {
            X = Pos.Right(_summaryCountLabel),
            Y = 2,
            Width = Dim.Auto(),
            Height = 1,
            CanFocus = false
        };

        _summaryAuthorLabel = new Label
        {
            X = Pos.Right(_summaryBookLabel),
            Y = 2,
            Width = Dim.Auto(),
            Height = 1,
            CanFocus = false
        };

        _headerLabel = new Label
        {
            X = TableHorizontalPadding,
            Y = 4,
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Height = 1,
            Text = FormatHeader(_tableLayout),
            CanFocus = false
        };
        _headerLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.TextMuted, TuiTheme.Palette.Background)));

        _headerRuleLabel = new Label
        {
            X = TableHorizontalPadding,
            Y = 5,
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Height = 1,
            Text = string.Empty,
            CanFocus = false
        };
        _headerRuleLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.Border, TuiTheme.Palette.Background)));

        _highlightRows = new ObservableCollection<string>();
        _highlightList = new ShortcutListView
        {
            X = TableHorizontalPadding,
            Y = 6,
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Height = Dim.Fill(1),
            CanFocus = true
        };
        _highlightList.SetSource(_highlightRows);
        _highlightList.ValueChanged += (_, _) =>
        {
            if (!_updatingViewState && _highlightList.SelectedItem is int selectedItem)
            {
                SelectedIndex = Math.Clamp(selectedItem, 0, Math.Max(0, _highlights.Count - 1));
            }
        };
        _highlightList.Accepting += async (_, _) => await HandleEnterFromHighlightsAsync().ConfigureAwait(false);
        _highlightList.KeyDown += async (_, key) => await HandleListKeyDownAsync(key).ConfigureAwait(false);
        _highlightList.ShortcutKeyPressed += async (_, key) => await HandleListKeyDownAsync(key).ConfigureAwait(false);

        _actionRows = new ObservableCollection<string>();
        _actionList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };
        _actionList.SetSource(_actionRows);
        _actionList.ValueChanged += (_, _) =>
        {
            if (_actionList.SelectedItem is int selectedItem)
            {
                ActionMenuIndex = Math.Clamp(selectedItem, 0, Math.Max(0, GetActionLabels().Count - 1));
            }
        };
        _actionList.Accepting += async (_, _) => await HandleActionMenuEnterAsync().ConfigureAwait(false);
        _actionList.KeyDown += async (_, key) => await HandleActionMenuKeyDownAsync(key).ConfigureAwait(false);

        _actionMenuFrame = new FrameView
        {
            X = Pos.AnchorEnd(36),
            Y = 6,
            Width = 34,
            Height = 8,
            Title = "Actions",
            CanFocus = true,
            Visible = false
        };
        _actionMenuFrame.Add(_actionList);

        _weightScaleLabel = new Label
        {
            X = 2,
            Y = 1,
            Width = Dim.Fill(4),
            Height = 1,
            CanFocus = false
        };

        _weightHelpLabel = new Label
        {
            X = 2,
            Y = 3,
            Width = Dim.Fill(4),
            Height = 2,
            Text = "Use ← → or 1-5, Enter to save, Esc to cancel.",
            CanFocus = false
        };

        _weightEditorFrame = new FrameView
        {
            X = Pos.Center() - (WeightEditorWidth / 2),
            Y = Pos.Center() - 3,
            Width = WeightEditorWidth,
            Height = WeightEditorHeight,
            Title = "Set Weight",
            CanFocus = true,
            Visible = false
        };
        _weightEditorFrame.Add(_weightScaleLabel, _weightHelpLabel);
        _weightEditorFrame.KeyDown += async (_, key) => await HandleWeightEditorKeyDownAsync(key).ConfigureAwait(false);

        _deleteConfirmationFrame = new FrameView
        {
            X = Pos.Center() - 24,
            Y = Pos.Center() - 2,
            Width = 48,
            Height = 5,
            Title = "Confirm Delete",
            CanFocus = true,
            Visible = false
        };
        _deleteConfirmationFrame.Add(new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = 2,
            Text = "Delete this highlight? Press Y to confirm or N/Esc to cancel.",
            CanFocus = false
        });
        _deleteConfirmationFrame.KeyDown += async (_, key) => await HandleDeleteConfirmationKeyDownAsync(key).ConfigureAwait(false);

        _previewLabel = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(2),
            Height = Dim.Fill(),
            CanFocus = false
        };

        _highlightPreviewFrame = new FrameView
        {
            X = Pos.Center() - (PreviewPopupWidth / 2),
            Y = Pos.Center() - (PreviewPopupHeight / 2),
            Width = PreviewPopupWidth,
            Height = PreviewPopupHeight,
            Title = "Highlight",
            CanFocus = true,
            Visible = false
        };
        _highlightPreviewFrame.Add(_previewLabel);
        _highlightPreviewFrame.KeyDown += async (_, key) => await HandleHighlightPreviewKeyDownAsync(key).ConfigureAwait(false);

        _statusLabel = new Label
        {
            X = TableHorizontalPadding,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Height = 1,
            Visible = false,
            CanFocus = false
        };
        _statusLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.TextMuted, TuiTheme.Palette.Background)));

        container.KeyDown += async (_, key) => await HandleContainerKeyDownAsync(key).ConfigureAwait(false);
        container.SubViewsLaidOut += (_, _) => UpdateTableLayout();
        _highlightList.ViewportChanged += (_, _) => UpdateTableLayout();
        container.Add(
            _titleLabel,
            _authorLabel,
            _summaryCountLabel,
            _summaryBookLabel,
            _summaryAuthorLabel,
            _headerLabel,
            _headerRuleLabel,
            _highlightList,
            _statusLabel,
            _actionMenuFrame,
            _weightEditorFrame,
            _deleteConfirmationFrame,
            _highlightPreviewFrame);

        _viewCreated = true;
        UpdateTableLayout();
        UpdateViewState();

        return container;
    }

    public async Task<ScreenResult> HandleKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        if (DeleteConfirmationOpen)
        {
            var deleteResult = await HandleDeleteConfirmationAsync(key, cancellationToken).ConfigureAwait(false);
            UpdateViewStateIfCreated();
            return deleteResult;
        }

        if (WeightEditorOpen)
        {
            var weightResult = await HandleWeightEditorAsync(key, cancellationToken).ConfigureAwait(false);
            UpdateViewStateIfCreated();
            return weightResult;
        }

        if (HighlightPreviewOpen)
        {
            var previewResult = HandleHighlightPreview(key);
            UpdateViewStateIfCreated();
            return previewResult;
        }

        if (ActionMenuOpen)
        {
            var actionResult = await HandleActionMenuAsync(key, cancellationToken).ConfigureAwait(false);
            UpdateViewStateIfCreated();
            return actionResult;
        }

        ScreenResult result = key.Key switch
        {
            ConsoleKey.UpArrow => MoveSelection(-1),
            ConsoleKey.DownArrow => MoveSelection(1),
            ConsoleKey.Enter => OpenHighlightPreview(),
            ConsoleKey.A => OpenActionMenu(),
            ConsoleKey.R => await RefreshAsync(cancellationToken).ConfigureAwait(false),
            ConsoleKey.Q => ScreenResult.ConfirmQuit(),
            ConsoleKey.Escape => ScreenResult.Pop(),
            ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control) => ScreenResult.Quit(),
            _ => ScreenResult.Stay()
        };

        UpdateViewStateIfCreated();
        return result;
    }

    private async Task HandleContainerKeyDownAsync(Key key)
    {
        if (_highlights.Count > 0 || ActionMenuOpen || DeleteConfirmationOpen || WeightEditorOpen || HighlightPreviewOpen)
        {
            return;
        }

        if (!TryMapGlobalKey(key, out var mappedKey))
        {
            return;
        }

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleListKeyDownAsync(Key key)
    {
        if (!TryMapGlobalKey(key, out var mappedKey))
        {
            return;
        }

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleActionMenuKeyDownAsync(Key key)
    {
        if (!TryMapGlobalKey(key, out var mappedKey))
        {
            return;
        }

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleDeleteConfirmationKeyDownAsync(Key key)
    {
        if (!TryMapDeleteConfirmationKey(key, out var mappedKey))
        {
            return;
        }

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleWeightEditorKeyDownAsync(Key key)
    {
        if (!TryMapWeightEditorKey(key, out var mappedKey))
        {
            return;
        }

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleHighlightPreviewKeyDownAsync(Key key)
    {
        if (!TryMapHighlightPreviewKey(key, out var mappedKey))
        {
            return;
        }

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleEnterFromHighlightsAsync()
    {
        var result = await HandleKeyAsync(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleActionMenuEnterAsync()
    {
        var result = await HandleKeyAsync(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task<ScreenResult> HandleActionMenuAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        return key.Key switch
        {
            ConsoleKey.UpArrow => MoveActionSelection(-1),
            ConsoleKey.DownArrow => MoveActionSelection(1),
            ConsoleKey.Escape => CloseActionMenu(),
            ConsoleKey.Enter => await ExecuteSelectedActionAsync(cancellationToken).ConfigureAwait(false),
            ConsoleKey.Q => ScreenResult.ConfirmQuit(),
            ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control) => ScreenResult.Quit(),
            _ => ScreenResult.Stay()
        };
    }

    private async Task<ScreenResult> HandleWeightEditorAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.DownArrow:
                return AdjustPendingWeight(-1);
            case ConsoleKey.RightArrow:
            case ConsoleKey.UpArrow:
                return AdjustPendingWeight(1);
            case ConsoleKey.Escape:
                return CloseWeightEditor();
            case ConsoleKey.Enter:
                await ApplyPendingWeightAsync(cancellationToken).ConfigureAwait(false);
                return ScreenResult.Stay();
            case ConsoleKey.D1:
            case ConsoleKey.NumPad1:
                return SelectPendingWeight(1);
            case ConsoleKey.D2:
            case ConsoleKey.NumPad2:
                return SelectPendingWeight(2);
            case ConsoleKey.D3:
            case ConsoleKey.NumPad3:
                return SelectPendingWeight(3);
            case ConsoleKey.D4:
            case ConsoleKey.NumPad4:
                return SelectPendingWeight(4);
            case ConsoleKey.D5:
            case ConsoleKey.NumPad5:
                return SelectPendingWeight(5);
            case ConsoleKey.Q:
                return ScreenResult.ConfirmQuit();
            case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                return ScreenResult.Quit();
            default:
                return ScreenResult.Stay();
        }
    }

    private ScreenResult HandleHighlightPreview(ConsoleKeyInfo key)
    {
        return key.Key switch
        {
            ConsoleKey.Escape => CloseHighlightPreview(),
            ConsoleKey.Enter => CloseHighlightPreview(),
            ConsoleKey.A => CloseHighlightPreview(),
            ConsoleKey.Q => ScreenResult.ConfirmQuit(),
            ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control) => ScreenResult.Quit(),
            _ => ScreenResult.Stay()
        };
    }

    private async Task<ScreenResult> HandleDeleteConfirmationAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.N:
                DeleteConfirmationOpen = false;
                _statusMessage = null;
                return ScreenResult.Stay();
            case ConsoleKey.Y:
                await DeleteSelectedHighlightAsync(cancellationToken).ConfigureAwait(false);
                return ScreenResult.Stay();
            case ConsoleKey.Q:
                return ScreenResult.ConfirmQuit();
            case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                return ScreenResult.Quit();
            default:
                return ScreenResult.Stay();
        }
    }

    private async Task<ScreenResult> ExecuteSelectedActionAsync(CancellationToken cancellationToken)
    {
        switch (ActionMenuIndex)
        {
            case 0:
                return OpenWeightEditor();
            case 1:
                await ToggleHighlightExclusionAsync(cancellationToken).ConfigureAwait(false);
                return ScreenResult.Stay();
            case 2:
                await ToggleBookExclusionAsync(cancellationToken).ConfigureAwait(false);
                return ScreenResult.Stay();
            case 3:
                await ToggleAuthorExclusionAsync(cancellationToken).ConfigureAwait(false);
                return ScreenResult.Stay();
            case 4:
                DeleteConfirmationOpen = true;
                ActionMenuOpen = false;
                _statusMessage = null;
                return ScreenResult.Stay();
            default:
                return ScreenResult.Stay();
        }
    }

    private async Task<ScreenResult> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var highlights = new List<HighlightItemDto>();
            var page = 1;

            while (true)
            {
                var response = await _client.GetHighlightsAsync(page, DetailPageSize, query: null, cancellationToken).ConfigureAwait(false);
                highlights.AddRange(response.Items.Where(item => item.BookId == _bookId));

                if (response.Page * response.PageSize >= response.Total)
                {
                    break;
                }

                page++;
            }

            var exclusions = await _client.GetExclusionsAsync(cancellationToken).ConfigureAwait(false);
            var weights = await _client.GetWeightsAsync(cancellationToken).ConfigureAwait(false);
            var excludedHighlightIds = exclusions.Highlights.Select(highlight => highlight.Id).ToHashSet();
            var weightLookup = weights.ToDictionary(weight => weight.Id, weight => weight.Weight);

            _highlights.Clear();
            _highlights.AddRange(highlights.Select(item => new HighlightViewModel(
                item.Id,
                item.BookId,
                item.AuthorId,
                item.Text,
                item.BookTitle,
                item.AuthorName,
                excludedHighlightIds.Contains(item.Id),
                weightLookup.TryGetValue(item.Id, out var weight) ? weight : null)));

            _isBookExcluded = exclusions.Books.Any(book => book.Id == _bookId);
            _isAuthorExcluded = exclusions.Authors.Any(author => author.Id == _authorId);
            SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, _highlights.Count - 1));
            ActionMenuIndex = Math.Clamp(ActionMenuIndex, 0, Math.Max(0, GetActionLabels().Count - 1));
            ActionMenuOpen = false;
            WeightEditorOpen = false;
            DeleteConfirmationOpen = false;
            HighlightPreviewOpen = false;
            _previewText = null;
            _statusMessage = $"Reloaded {_highlights.Count.ToString(CultureInfo.InvariantCulture)} highlight(s).";
        }
        catch (HttpRequestException)
        {
            _statusMessage = "Cannot reach server. Check the connection.";
        }

        return ScreenResult.Stay();
    }

    private async Task ApplyPendingWeightAsync(CancellationToken cancellationToken)
    {
        var currentHighlight = GetSelectedHighlight();
        if (currentHighlight is null)
        {
            return;
        }

        using var response = await _client.PutWeightAsync(currentHighlight.Id, new SetWeightRequest { Weight = PendingWeight }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _statusMessage = $"Weight update failed: {(int)response.StatusCode} {response.ReasonPhrase}";
            return;
        }

        _highlights[SelectedIndex] = currentHighlight with { Weight = PendingWeight };
        WeightEditorOpen = false;
        _statusMessage = $"Weight updated to {PendingWeight.ToString(CultureInfo.InvariantCulture)}.";
    }

    private async Task ToggleHighlightExclusionAsync(CancellationToken cancellationToken)
    {
        var currentHighlight = GetSelectedHighlight();
        if (currentHighlight is null)
        {
            return;
        }

        using var response = currentHighlight.IsExcluded
            ? await _client.DeleteExcludeAsync("highlight", currentHighlight.Id, cancellationToken).ConfigureAwait(false)
            : await _client.PostExcludeAsync("highlight", currentHighlight.Id, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _statusMessage = $"Highlight update failed: {(int)response.StatusCode} {response.ReasonPhrase}";
            return;
        }

        _highlights[SelectedIndex] = currentHighlight with { IsExcluded = !currentHighlight.IsExcluded };
        ActionMenuOpen = false;
        _statusMessage = currentHighlight.IsExcluded ? "Highlight included." : "Highlight excluded.";
    }

    private async Task ToggleBookExclusionAsync(CancellationToken cancellationToken)
    {
        using var response = _isBookExcluded
            ? await _client.DeleteExcludeAsync("book", _bookId, cancellationToken).ConfigureAwait(false)
            : await _client.PostExcludeAsync("book", _bookId, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _statusMessage = $"Book update failed: {(int)response.StatusCode} {response.ReasonPhrase}";
            return;
        }

        _isBookExcluded = !_isBookExcluded;
        ActionMenuOpen = false;
        _statusMessage = _isBookExcluded ? "Book excluded." : "Book included.";
    }

    private async Task ToggleAuthorExclusionAsync(CancellationToken cancellationToken)
    {
        using var response = _isAuthorExcluded
            ? await _client.DeleteExcludeAsync("author", _authorId, cancellationToken).ConfigureAwait(false)
            : await _client.PostExcludeAsync("author", _authorId, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _statusMessage = $"Author update failed: {(int)response.StatusCode} {response.ReasonPhrase}";
            return;
        }

        _isAuthorExcluded = !_isAuthorExcluded;
        ActionMenuOpen = false;
        _statusMessage = _isAuthorExcluded ? "Author excluded." : "Author included.";
    }

    private async Task DeleteSelectedHighlightAsync(CancellationToken cancellationToken)
    {
        var currentHighlight = GetSelectedHighlight();
        if (currentHighlight is null)
        {
            DeleteConfirmationOpen = false;
            return;
        }

        using var response = await _client.DeleteHighlightAsync(currentHighlight.Id, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _statusMessage = $"Delete failed: {(int)response.StatusCode} {response.ReasonPhrase}";
            DeleteConfirmationOpen = false;
            return;
        }

        _highlights.RemoveAt(SelectedIndex);
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, _highlights.Count - 1));
        DeleteConfirmationOpen = false;
        ActionMenuOpen = false;
        WeightEditorOpen = false;
        HighlightPreviewOpen = false;
        _previewText = null;
        _statusMessage = "Highlight deleted.";
    }

    private ScreenResult MoveSelection(int delta)
    {
        if (_highlights.Count == 0)
        {
            SelectedIndex = 0;
            return ScreenResult.Stay();
        }

        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, _highlights.Count - 1);
        _statusMessage = null;
        return ScreenResult.Stay();
    }

    private ScreenResult MoveActionSelection(int delta)
    {
        ActionMenuIndex = Math.Clamp(ActionMenuIndex + delta, 0, Math.Max(0, GetActionLabels().Count - 1));
        return ScreenResult.Stay();
    }

    private ScreenResult AdjustPendingWeight(int delta)
    {
        PendingWeight = Math.Clamp(PendingWeight + delta, MinimumWeight, MaximumWeight);
        return ScreenResult.Stay();
    }

    private ScreenResult SelectPendingWeight(int weight)
    {
        PendingWeight = Math.Clamp(weight, MinimumWeight, MaximumWeight);
        return ScreenResult.Stay();
    }

    private ScreenResult OpenActionMenu()
    {
        if (_highlights.Count == 0)
        {
            return ScreenResult.Stay();
        }

        ActionMenuOpen = true;
        ActionMenuIndex = 0;
        _statusMessage = null;
        return ScreenResult.Stay();
    }

    private ScreenResult OpenWeightEditor()
    {
        var currentHighlight = GetSelectedHighlight();
        if (currentHighlight is null)
        {
            return ScreenResult.Stay();
        }

        PendingWeight = currentHighlight.Weight ?? DefaultWeight;
        ActionMenuOpen = false;
        WeightEditorOpen = true;
        HighlightPreviewOpen = false;
        _statusMessage = null;
        return ScreenResult.Stay();
    }

    private ScreenResult CloseWeightEditor()
    {
        WeightEditorOpen = false;
        _statusMessage = null;
        return ScreenResult.Stay();
    }

    private ScreenResult OpenHighlightPreview()
    {
        var currentHighlight = GetSelectedHighlight();
        if (currentHighlight is null)
        {
            return ScreenResult.Stay();
        }

        _previewText = currentHighlight.Text;
        HighlightPreviewOpen = true;
        ActionMenuOpen = false;
        WeightEditorOpen = false;
        DeleteConfirmationOpen = false;
        _statusMessage = null;
        return ScreenResult.Stay();
    }

    private ScreenResult CloseHighlightPreview()
    {
        HighlightPreviewOpen = false;
        _previewText = null;
        return ScreenResult.Stay();
    }

    private ScreenResult CloseActionMenu()
    {
        ActionMenuOpen = false;
        _statusMessage = null;
        return ScreenResult.Stay();
    }

    private HighlightViewModel? GetSelectedHighlight()
        => SelectedIndex >= 0 && SelectedIndex < _highlights.Count ? _highlights[SelectedIndex] : null;

    private bool IsEffectivelyExcluded(HighlightViewModel highlight)
        => highlight.IsExcluded || _isBookExcluded || _isAuthorExcluded;

    private List<string> GetActionLabels()
        =>
        [
            "Set weight",
            GetSelectedHighlight()?.IsExcluded == true ? "Include highlight" : "Exclude highlight",
            _isBookExcluded ? "Include book" : "Exclude book",
            _isAuthorExcluded ? "Include author" : "Exclude author",
            "Delete highlight"
        ];

    private void UpdateViewStateIfCreated()
    {
        if (_viewCreated)
        {
            UpdateViewState();
        }
    }

    private void UpdateViewState()
    {
        _updatingViewState = true;
        try
        {
            if (_titleLabel is not null)
            {
                _titleLabel.Text = _bookTitle;
            }

            if (_authorLabel is not null)
            {
                _authorLabel.Text = $"by {_authorName}";
            }

            var highlightCount = _highlights.Count == 1 ? "1 highlight" : $"{_highlights.Count.ToString(CultureInfo.InvariantCulture)} highlights";

            if (_summaryCountLabel is not null)
            {
                _summaryCountLabel.Text = highlightCount;
            }

            if (_summaryBookLabel is not null)
            {
                _summaryBookLabel.Text = $"  |  Book: {(_isBookExcluded ? "excluded" : "included")}";
                _summaryBookLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
                    _isBookExcluded ? TuiTheme.Palette.Error : TuiTheme.Palette.TextMuted,
                    TuiTheme.Palette.Background)));
            }

            if (_summaryAuthorLabel is not null)
            {
                _summaryAuthorLabel.Text = $"  |  Author: {(_isAuthorExcluded ? "excluded" : "included")}";
                _summaryAuthorLabel.SetScheme(new Terminal.Gui.Drawing.Scheme(new Terminal.Gui.Drawing.Attribute(
                    _isAuthorExcluded ? TuiTheme.Palette.Error : TuiTheme.Palette.TextMuted,
                    TuiTheme.Palette.Background)));
            }

            if (_headerLabel is not null)
            {
                _headerLabel.Text = FormatHeader(_tableLayout);
            }

            if (_highlightRows is not null)
            {
                _highlightRows.Clear();
                foreach (var row in BuildHighlightRows())
                {
                    _highlightRows.Add(row);
                }
            }

            if (_highlightList is not null)
            {
                _highlightList.SelectedItem = _highlightRows is { Count: > 0 }
                    ? Math.Clamp(SelectedIndex, 0, _highlightRows.Count - 1)
                    : 0;
            }

            if (_actionRows is not null)
            {
                _actionRows.Clear();
                foreach (var action in GetActionLabels())
                {
                    _actionRows.Add(action);
                }
            }

            if (_actionMenuFrame is not null)
            {
                _actionMenuFrame.Visible = ActionMenuOpen;
            }

            if (_actionList is not null && _actionRows is { Count: > 0 })
            {
                _actionList.SelectedItem = Math.Clamp(ActionMenuIndex, 0, _actionRows.Count - 1);
            }

            if (_weightEditorFrame is not null)
            {
                _weightEditorFrame.Visible = WeightEditorOpen;
            }

            if (_weightScaleLabel is not null)
            {
                _weightScaleLabel.Text = $"Selected: {PendingWeight}    {BuildWeightScale()}";
            }

            if (_weightHelpLabel is not null)
            {
                _weightHelpLabel.Text = "Use \u2190 \u2192 or 1-5, Enter to save, Esc to cancel.";
            }

            if (_deleteConfirmationFrame is not null)
            {
                _deleteConfirmationFrame.Visible = DeleteConfirmationOpen;
            }

            if (_highlightPreviewFrame is not null)
            {
                _highlightPreviewFrame.Visible = HighlightPreviewOpen;
            }

            if (_previewLabel is not null)
            {
                _previewLabel.Text = HighlightPreviewOpen
                    ? string.Join("\n", WrapPreviewText(_previewText ?? string.Empty, PreviewContentWidth))
                    : string.Empty;
            }

            if (_statusLabel is not null)
            {
                _statusLabel.Text = _statusMessage ?? string.Empty;
                _statusLabel.Visible = !string.IsNullOrWhiteSpace(_statusMessage);
            }

            if (DeleteConfirmationOpen)
            {
                _deleteConfirmationFrame?.SetFocus();
            }
            else if (WeightEditorOpen)
            {
                _weightEditorFrame?.SetFocus();
            }
            else if (HighlightPreviewOpen)
            {
                _highlightPreviewFrame?.SetFocus();
            }
            else if (ActionMenuOpen)
            {
                _actionList?.SetFocus();
            }
            else if (_highlights.Count > 0)
            {
                _highlightList?.SetFocus();
            }
        }
        finally
        {
            _updatingViewState = false;
        }
    }

    private void UpdateTableLayout()
    {
        if (!_viewCreated || _highlightList is null || _headerLabel is null || _headerRuleLabel is null)
        {
            return;
        }

        var availableWidth = Math.Max(_highlightList.Viewport.Width, _headerLabel.Viewport.Width);
        if (availableWidth <= 0)
        {
            return;
        }

        var nextLayout = CalculateTableLayout(availableWidth);
        if (nextLayout == _tableLayout && _headerRuleLabel.Text?.Length == availableWidth)
        {
            return;
        }

        _tableLayout = nextLayout;
        _headerLabel.Text = FormatHeader(_tableLayout);
        _headerRuleLabel.Text = new string('-', availableWidth);

        if (_highlightRows is not null)
        {
            _highlightRows.Clear();
            foreach (var row in BuildHighlightRows())
            {
                _highlightRows.Add(row);
            }
        }
    }

    private IEnumerable<string> BuildHighlightRows()
    {
        if (_highlights.Count == 0)
        {
            yield return "This book has no highlights.";
            yield break;
        }

        foreach (var highlight in _highlights)
        {
            yield return FormatHighlightRow(highlight);
        }
    }

    private string FormatHighlightRow(HighlightViewModel highlight)
    {
        var excluded = IsEffectivelyExcluded(highlight);
        var dot = excluded ? "\u2022" : " ";
        var text = FitCell(highlight.Text.ReplaceLineEndings(" "), _tableLayout.HighlightWidth);
        var weight = (highlight.Weight ?? DefaultWeight).ToString(CultureInfo.InvariantCulture).PadLeft(_tableLayout.WeightWidth);
        return $"{weight}  {dot}  {text}".TrimEnd();
    }

    private string BuildWeightScale()
        => string.Join("  ", Enumerable.Range(MinimumWeight, MaximumWeight)
            .Select(weight => weight == PendingWeight ? $"[{weight}]" : $" {weight} "));

    private static IEnumerable<string> WrapPreviewText(string text, int width)
    {
        if (width <= 0)
        {
            yield return text;
            yield break;
        }

        foreach (var paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                yield return string.Empty;
                continue;
            }

            var lineBuilder = new StringBuilder();
            foreach (var segment in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = segment;
                while (word.Length > width)
                {
                    if (lineBuilder.Length > 0)
                    {
                        yield return lineBuilder.ToString();
                        lineBuilder.Clear();
                    }

                    yield return word[..width];
                    word = word[width..];
                }

                if (lineBuilder.Length == 0)
                {
                    lineBuilder.Append(word);
                }
                else if (lineBuilder.Length + 1 + word.Length <= width)
                {
                    lineBuilder.Append(' ').Append(word);
                }
                else
                {
                    yield return lineBuilder.ToString();
                    lineBuilder.Clear();
                    lineBuilder.Append(word);
                }
            }

            if (lineBuilder.Length > 0)
            {
                yield return lineBuilder.ToString();
            }
        }
    }

    private static string FormatHeader(TableLayout tableLayout)
        => $"{FitCell("WEIGHT", tableLayout.WeightWidth)}  {" ",DotColumnWidth}  {FitCell("HIGHLIGHT", tableLayout.HighlightWidth)}";

    private void ApplyNavigation(ScreenResult result)
    {
        if (result.Action != ScreenAction.None)
        {
            _navigate?.Invoke(result);
        }
    }

    private static bool TryMapGlobalKey(Key key, out ConsoleKeyInfo mappedKey)
    {
        switch (key.KeyCode)
        {
            case KeyCode.Esc:
                mappedKey = new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false);
                return true;
            case KeyCode.Q:
                mappedKey = new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false);
                return true;
            case var keyCode when keyCode == (KeyCode.C | KeyCode.CtrlMask):
                mappedKey = new ConsoleKeyInfo('c', ConsoleKey.C, false, false, true);
                return true;
        }

        var rune = key.AsRune.Value;
        if (rune is 'q' or 'Q')
        {
            mappedKey = new ConsoleKeyInfo((char)rune, ConsoleKey.Q, char.IsUpper((char)rune), false, false);
            return true;
        }

        if (rune is 'r' or 'R')
        {
            mappedKey = new ConsoleKeyInfo((char)rune, ConsoleKey.R, char.IsUpper((char)rune), false, false);
            return true;
        }

        if (rune is 'a' or 'A')
        {
            mappedKey = new ConsoleKeyInfo((char)rune, ConsoleKey.A, char.IsUpper((char)rune), false, false);
            return true;
        }

        mappedKey = default;
        return false;
    }

    private static bool TryMapWeightEditorKey(Key key, out ConsoleKeyInfo mappedKey)
    {
        switch (key.KeyCode)
        {
            case KeyCode.CursorLeft:
                mappedKey = new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false);
                return true;
            case KeyCode.CursorRight:
                mappedKey = new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false);
                return true;
            case KeyCode.CursorUp:
                mappedKey = new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);
                return true;
            case KeyCode.CursorDown:
                mappedKey = new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false);
                return true;
            case KeyCode.Enter:
                mappedKey = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
                return true;
        }

        if (TryMapGlobalKey(key, out mappedKey))
        {
            return true;
        }

        mappedKey = key.AsRune.Value switch
        {
            '1' => new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false),
            '2' => new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false),
            '3' => new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, false),
            '4' => new ConsoleKeyInfo('4', ConsoleKey.D4, false, false, false),
            '5' => new ConsoleKeyInfo('5', ConsoleKey.D5, false, false, false),
            _ => default
        };

        return mappedKey.Key != 0;
    }

    private static bool TryMapHighlightPreviewKey(Key key, out ConsoleKeyInfo mappedKey)
    {
        if (key.KeyCode == KeyCode.Enter)
        {
            mappedKey = new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
            return true;
        }

        return TryMapGlobalKey(key, out mappedKey);
    }

    private static bool TryMapDeleteConfirmationKey(Key key, out ConsoleKeyInfo mappedKey)
    {
        if (TryMapGlobalKey(key, out mappedKey))
        {
            return true;
        }

        var rune = key.AsRune.Value;
        switch (rune)
        {
            case 'y':
            case 'Y':
                mappedKey = new ConsoleKeyInfo((char)rune, ConsoleKey.Y, rune == 'Y', false, false);
                return true;
            case 'n':
            case 'N':
                mappedKey = new ConsoleKeyInfo((char)rune, ConsoleKey.N, rune == 'N', false, false);
                return true;
            default:
                mappedKey = default;
                return false;
        }
    }

    private static TableLayout CalculateTableLayout(int availableWidth)
    {
        const int spacingWidth = 4;

        var highlightWidth = availableWidth - WeightColumnWidth - DotColumnWidth - spacingWidth;
        if (highlightWidth < MinimumHighlightColumnWidth)
        {
            highlightWidth = Math.Max(0, availableWidth - WeightColumnWidth - DotColumnWidth - spacingWidth);
        }

        return new TableLayout(
            Math.Max(0, highlightWidth),
            WeightColumnWidth);
    }

    private static string FitCell(string value, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (value.Length <= width)
        {
            return value.PadRight(width);
        }

        if (width <= 3)
        {
            return value[..width];
        }

        return value[..(width - 3)] + "...";
    }

}
