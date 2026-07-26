using System.Collections.ObjectModel;
using System.Globalization;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Relego.Cli.Infrastructure;
using Relego.Core.Sources;
using Relego.Cli.Import;
using Relego.Cli.Tui.ViewModels;
using Relego.Core.Contracts;

namespace Relego.Cli.Tui;

public sealed class BookListScreen(
    RelegoHttpClient client,
    ClippingsImportWorkflow syncWorkflow,
    Action? onConnectionFailure = null,
    Func<CancellationToken, Task>? refreshConnectionStatusAsync = null) : IScreen
{
    private static readonly IReadOnlyList<(string Key, string Label)> DefaultKeyHints =
    [
        ("↑↓", "Navigate"),
        ("Enter", "View"),
        ("I", "Import"),
        ("N", "Rename book title"),
        ("S", "Settings"),
        ("/", "Search"),
        ("R", "Refresh"),
        ("Q", "Quit")
    ];

    private static readonly IReadOnlyList<(string Key, string Label)> RenamePromptKeyHints =
    [
        ("Enter", "Save"),
        ("Esc", "Cancel")
    ];

    private const int PageSize = 100;
    private const int DefaultTableWidth = 80;
    private const int HighlightsColumnWidth = 10;
    private const int MinimumTitleColumnWidth = 18;
    private const int MinimumAuthorColumnWidth = 18;
    private const int PreferredAuthorColumnWidth = 30;
    private const int TableHorizontalPadding = 2;
    private const string SectionTitle = "Books";
    private const int RenamePopupWidth = 68;
    private const int RenamePopupHeight = 7;
    private const string SearchPlaceholderIdle = "type / to search";
    private const string SearchPlaceholderFocused = "Press Esc to return to the list";
    private const string SyncPlaceholderDetected = "Press Enter to import or edit the path";
    private const string SyncPlaceholderManual = "Enter a Kindle .txt or Kobo SQLite path";

    private readonly RelegoHttpClient _client = client;
    private readonly ClippingsImportWorkflow _syncWorkflow = syncWorkflow;
    private readonly Action? _onConnectionFailure = onConnectionFailure;
    private readonly Func<CancellationToken, Task>? _refreshConnectionStatusAsync = refreshConnectionStatusAsync;
    private List<BookViewModel> _books = [];
    private List<BookViewModel> _filteredBooks = [];
    private int _selectedIndex;
    private bool _isSearchActive;
    private bool _isRenamePromptActive;
    private string _searchQuery = string.Empty;
    private string _syncPathInput = string.Empty;
    private string _renameInput = string.Empty;
    private string? _detectedSyncPath;
    private bool _hasDetectedSyncPath;
    private FrameView? _searchFrame;
    private SearchTextField? _searchField;
    private Label? _searchPlaceholder;
    private string? _errorMessage;
    private Label? _errorLabel;
    private Label? _feedbackLabel;
    private bool _viewHasBooksList;
    private bool _hasRenamePromptView;
    private string? _feedbackMessage;
    private bool _feedbackIsError;
    private Label? _renameBackdrop;
    private FrameView? _renameFrame;
    private SearchTextField? _renameField;
    private Action<ScreenResult>? _navigate;
    private Action? _refreshVisibleBooks;
    private Action? _uiStateObserver;
    private ShortcutListView? _listView;
    private ToolbarMode _toolbarMode;

    public IReadOnlyList<BookViewModel> Books => _books;

    public IReadOnlyList<BookViewModel> FilteredBooks => _filteredBooks;

    public int SelectedIndex => _selectedIndex;

    public bool IsSearchActive => _isSearchActive;

    public bool IsImportPromptActive => _toolbarMode == ToolbarMode.SyncPath;

    public bool IsRenamePromptActive => _isRenamePromptActive;

    public string SearchQuery => _searchQuery;

    public string ImportPathInput => _syncPathInput;

    public string? FeedbackMessage => _feedbackMessage;

    public bool FeedbackIsError => _feedbackIsError;

    public string Title => string.Empty;

    public IReadOnlyList<(string Key, string Label)> KeyHints => _isRenamePromptActive ? RenamePromptKeyHints : DefaultKeyHints;

    private readonly record struct TableLayout(int TitleWidth, int AuthorWidth, int HighlightsWidth);

    private enum ToolbarMode
    {
        Search,
        SyncPath
    }

    public int ToolbarHeight => 4;

    public void RegisterUiStateObserver(Action? onStateChanged)
    {
        _uiStateObserver = onStateChanged;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the window hierarchy")]
    public View? CreateToolbarView(Action<ScreenResult> navigate)
    {
        var container = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 4,
            CanFocus = true
        };

        _searchFrame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 3,
            BorderStyle = LineStyle.Rounded,
            Title = string.Empty,
            CanFocus = true
        };
        _searchFrame.SetScheme(CreateSearchFrameScheme(isFocused: false));

        var palette = TuiTheme.Palette;
        var fieldAttribute = new Terminal.Gui.Drawing.Attribute(
            palette.Text, palette.Background);

        _searchField = new SearchTextField
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(2),
            Height = 1,
            CanFocus = true,
            Text = GetToolbarText()
        };
        _searchField.SetScheme(CreateSearchFieldScheme(fieldAttribute));
        _searchField.TextChanged += (_, _) => HandleToolbarTextChanged();
        _searchField.HasFocusChanged += (_, _) => UpdateToolbarChrome();
        _searchField.Accepting += async (_, _) => await HandleToolbarSubmitAsync().ConfigureAwait(false);
        _searchField.KeyDown += (_, key) => HandleToolbarKeyDown(key);

        _searchPlaceholder = new Label
        {
            X = 2,
            Y = 0,
            Width = Dim.Fill(3),
            Height = 1,
            CanFocus = false,
            Text = GetToolbarPlaceholder(hasFocus: false),
            Visible = string.IsNullOrEmpty(GetToolbarText())
        };
        _searchPlaceholder.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
            palette.TextMuted, palette.Background)));

        _searchFrame.Add(_searchField, _searchPlaceholder);
        container.Add(_searchFrame);

        _feedbackLabel = CreateToolbarFeedbackLabel();
        container.Add(_feedbackLabel);

        UpdateToolbarChrome();
        return container;
    }

    private static Scheme CreateSearchFrameScheme(bool isFocused)
    {
        var palette = TuiTheme.Palette;
        var borderColor = isFocused
            ? palette.BorderFocus
            : palette.Border;

        return new Scheme(new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background))
        {
            Normal = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background),
            Focus = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background),
            Active = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background),
            HotNormal = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background),
            HotFocus = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background),
            HotActive = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background),
            Disabled = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background)
        };
    }

    private static Scheme CreateSearchFieldScheme(Terminal.Gui.Drawing.Attribute attribute) => new(attribute)
    {
        Normal = attribute,
        Focus = attribute,
        Active = attribute,
        Code = attribute,
        Editable = attribute,
        Highlight = attribute,
        HotActive = attribute,
        HotFocus = attribute,
        HotNormal = attribute,
        ReadOnly = attribute,
        Disabled = attribute
    };

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var highlights = await LoadAllHighlightsAsync(cancellationToken).ConfigureAwait(false);
            var exclusions = await _client.GetExclusionsAsync(cancellationToken).ConfigureAwait(false);
            var weights = await _client.GetWeightsAsync(cancellationToken).ConfigureAwait(false);

            _books = EnrichBooks(BookViewModel.FromHighlights(highlights), exclusions, weights);
            _errorMessage = null;

            if (_refreshConnectionStatusAsync is not null)
            {
                await _refreshConnectionStatusAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException)
        {
            _books = [];
            _errorMessage = "Cannot reach server. Check the connection.";
            _onConnectionFailure?.Invoke();
        }

        ApplyFilter();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the parent container hierarchy")]
    public View CreateView(Action<ScreenResult> navigate)
    {
        _navigate = navigate;

        var container = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };

        if (_filteredBooks.Count == 0 && !_isSearchActive)
        {
            _viewHasBooksList = false;
            _listView = null;

            if (_errorMessage is null)
            {
                var emptyLabel = new Label
                {
                    Text = "No books imported yet. Press I to import highlights.",
                    X = Pos.Center(),
                    Y = Pos.Center()
                };
                container.Add(emptyLabel);
            }

            _errorLabel = new Label
            {
                Text = _errorMessage ?? string.Empty,
                Visible = _errorMessage is not null,
                X = Pos.Center(),
                Y = Pos.Center()
            };
            _errorLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
                TuiTheme.Palette.Error, TuiTheme.Palette.Background)));
            container.Add(_errorLabel);

            void RefreshEmpty()
            {
                if (_errorLabel is not null)
                {
                    _errorLabel.Text = _errorMessage ?? string.Empty;
                    _errorLabel.Visible = _errorMessage is not null;
                }

                UpdateFeedbackLabel();
            }

            _refreshVisibleBooks = RefreshEmpty;
            SetupContainerKeyBindings(container, null, navigate, RefreshEmpty, BeginSearchInput, () => BeginImportPrompt());
            return container;
        }

        _viewHasBooksList = true;

        var tableLayout = CalculateTableLayout(DefaultTableWidth);

        var titleLabel = new Label
        {
            Text = SectionTitle,
            X = TableHorizontalPadding,
            Y = 0,
            Width = Dim.Fill(TableHorizontalPadding * 2)
        };
        titleLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.AccentText, TuiTheme.Palette.Background)));

        var headerLabel = new Label
        {
            Text = FormatHeader(tableLayout),
            X = TableHorizontalPadding,
            Y = 1,
            Width = Dim.Fill(TableHorizontalPadding * 2)
        };
        headerLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.TextMuted, TuiTheme.Palette.Background)));

        var headerRuleLabel = new Label
        {
            X = TableHorizontalPadding,
            Y = 2,
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Text = string.Empty
        };
        headerRuleLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.Border, TuiTheme.Palette.Background)));

        var displayItems = new ObservableCollection<string>(
            _filteredBooks.Select(book => FormatBookRow(book, tableLayout)));

        var listView = new ShortcutListView
        {
            X = TableHorizontalPadding,
            Y = 3,
            Width = Dim.Fill(TableHorizontalPadding * 2),
            Height = Dim.Fill(),
            CanFocus = true
        };
        listView.SetSource(displayItems);

        if (_filteredBooks.Count > 0)
        {
            listView.SelectedItem = Math.Min(_selectedIndex, _filteredBooks.Count - 1);
        }

        listView.Accepting += (_, _) =>
        {
            var selected = GetSelectedBook();
            if (selected is not null)
            {
                navigate(ScreenResult.Push(new HighlightDetailScreen(selected, _client)));
            }
        };

        void RefreshVisibleBooks()
        {
            // Snapshot selection before Clear: the ObservableCollection change fires
            // ValueChanged on the ListView, which would overwrite _selectedIndex to 0.
            var selectedIndex = _selectedIndex;

            displayItems.Clear();
            foreach (var item in _filteredBooks.Select(book => FormatBookRow(book, tableLayout)))
            {
                displayItems.Add(item);
            }

            if (_filteredBooks.Count > 0)
            {
                listView.SelectedItem = Math.Min(selectedIndex, _filteredBooks.Count - 1);
            }
            else
            {
                _selectedIndex = 0;
            }

            UpdateErrorLabel();
            UpdateFeedbackLabel();
        }

        void UpdateTableLayout()
        {
            var availableWidth = Math.Max(listView.Viewport.Width, headerLabel.Viewport.Width);
            if (availableWidth <= 0)
            {
                return;
            }

            var nextLayout = CalculateTableLayout(availableWidth);
            if (nextLayout == tableLayout)
            {
                return;
            }

            tableLayout = nextLayout;
            headerLabel.Text = FormatHeader(tableLayout);
            headerRuleLabel.Text = new string('-', availableWidth);
            RefreshVisibleBooks();
        }

        UpdateTableLayout();

        _renameBackdrop = ModalChrome.CreateBackdrop();

        var palette = TuiTheme.Palette;
        var fieldAttribute = new Terminal.Gui.Drawing.Attribute(palette.Text, palette.Background);

        _renameField = new SearchTextField
        {
            X = 2,
            Y = 2,
            Width = Dim.Fill(4),
            Height = 1,
            CanFocus = true,
            Text = _renameInput
        };
        _renameField.SetScheme(CreateSearchFieldScheme(fieldAttribute));
        _renameField.TextChanged += (_, _) => _renameInput = _renameField.Text ?? string.Empty;
        _renameField.HasFocusChanged += (_, _) => UpdateRenamePromptState();
        _renameField.Accepting += async (_, _) => await SubmitRenameAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
        _renameField.KeyDown += (_, key) => HandleRenamePromptKeyDown(key);

        var renameHelpLabel = new Label
        {
            X = 2,
            Y = 4,
            Width = Dim.Fill(4),
            Height = 1,
            Text = "Press Enter to save or Esc to cancel.",
            CanFocus = false
        };
        renameHelpLabel.SetScheme(ModalChrome.CreateMutedTextScheme());

        _renameFrame = ModalChrome.CreateFrame(RenamePopupWidth, RenamePopupHeight);
        _renameFrame.KeyDown += (_, key) => HandleRenamePromptKeyDown(key);
        _renameFrame.Add(_renameField, renameHelpLabel);
        _hasRenamePromptView = true;

        container.SubViewsLaidOut += (_, _) => UpdateTableLayout();
        listView.ViewportChanged += (_, _) => UpdateTableLayout();

        container.Add(titleLabel, headerLabel, headerRuleLabel, listView, _renameBackdrop, _renameFrame);
        _listView = listView;
        _refreshVisibleBooks = RefreshVisibleBooks;
        SetupContainerKeyBindings(container, listView, navigate, RefreshVisibleBooks, BeginSearchInput, () => BeginImportPrompt());

        // Save/restore selection around SetFocus: Terminal.Gui may fire ValueChanged
        // with SelectedItem = 0 when the ListView gains focus, overwriting _selectedIndex.
        var savedIndex = _selectedIndex;
        listView.SetFocus();
        _selectedIndex = savedIndex;
        if (_filteredBooks.Count > 0)
            listView.SelectedItem = Math.Min(savedIndex, _filteredBooks.Count - 1);

        UpdateRenamePromptState();

        return container;
    }

    public void MoveSelection(int delta)
    {
        if (_filteredBooks.Count == 0)
        {
            _selectedIndex = 0;
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _filteredBooks.Count - 1);
    }

    private void UpdateErrorLabel()
    {
        if (_errorLabel is not null)
        {
            _errorLabel.Text = _errorMessage ?? string.Empty;
            _errorLabel.Visible = _errorMessage is not null;
        }
    }

    public void ActivateSearch()
    {
        _toolbarMode = ToolbarMode.Search;
        _isSearchActive = true;
        ApplyFilter();
    }

    public void DeactivateSearch()
    {
        _toolbarMode = ToolbarMode.Search;
        _isSearchActive = false;
        _searchQuery = string.Empty;

        if (_searchField is not null)
        {
            _searchField.Text = string.Empty;
        }

        ApplyFilter();
        UpdateToolbarChrome();
    }

    public void BeginImportPrompt(string? detectedPath = null)
    {
        _toolbarMode = ToolbarMode.SyncPath;
        _isSearchActive = false;

        var resolvedDetectedPath = detectedPath ?? KindleDetector.DetectClippingsPath();
        _detectedSyncPath = resolvedDetectedPath;
        _hasDetectedSyncPath = !string.IsNullOrWhiteSpace(resolvedDetectedPath);
        _syncPathInput = ResolveDefaultSyncPath(resolvedDetectedPath) ?? string.Empty;

        if (_searchField is not null)
        {
            _searchField.Text = _syncPathInput;
            _searchField.SetFocus();
            _searchField.MoveEnd();
        }

        UpdateToolbarChrome();
    }

    public void CancelImportPrompt()
    {
        _toolbarMode = ToolbarMode.Search;
        _detectedSyncPath = null;
        _hasDetectedSyncPath = false;
        _syncPathInput = string.Empty;

        if (_searchField is null)
        {
            return;
        }

        RestoreToolbarAfterImportPrompt();
    }

    private void RestoreToolbarAfterImportPrompt()
    {
        ArgumentNullException.ThrowIfNull(_searchField);

        _searchField.Text = _searchQuery;

        if (_listView is not null)
        {
            _listView.SetFocus();
        }
        else
        {
            _searchField.SetFocus();
        }

        UpdateToolbarChrome();
    }

    public void BeginRenamePrompt(BookViewModel book)
    {
        _isRenamePromptActive = true;
        _renameInput = book.Title;

        if (_hasRenamePromptView)
        {
            ShowRenamePromptView();
        }

        NotifyUiStateChanged();
    }

    public void CancelRenamePrompt()
    {
        _isRenamePromptActive = false;
        _renameInput = string.Empty;

        if (_hasRenamePromptView)
        {
            HideRenamePromptView();
        }

        NotifyUiStateChanged();
    }

    public async Task SubmitRenameAsync(string? newTitle = null, CancellationToken cancellationToken = default)
    {
        var resolvedTitle = (newTitle ?? _renameInput).Trim();

        if (string.IsNullOrWhiteSpace(resolvedTitle))
        {
            SetFeedback("Enter a title or press Esc to cancel.", isError: true);
            return;
        }

        var book = GetSelectedBook();
        if (book is null)
        {
            CancelRenamePrompt();
            return;
        }

        HttpResponseMessage response;
        try
        {
            response = await _client.RenameBookAsync(book.BookId, new Core.Contracts.RenameBookRequest { Title = resolvedTitle }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            SetFeedback("Cannot reach server.", isError: true);
            CancelRenamePrompt();
            return;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            SetFeedback($"Book {book.BookId} not found.", isError: true);
            CancelRenamePrompt();
            return;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            SetFeedback($"A book titled \"{resolvedTitle}\" by the same author already exists.", isError: true);
            CancelRenamePrompt();
            return;
        }

        response.EnsureSuccessStatusCode();

        CancelRenamePrompt();
        SetFeedback($"Book renamed to \"{resolvedTitle}\".", isError: false);

        // Reload so the list reflects the new title
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        _refreshVisibleBooks?.Invoke();
    }

    public async Task<ClippingsImportOutcome> SubmitImportAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        var resolvedPath = filePath ?? _syncPathInput;
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            SetFeedback("Enter a Kindle .txt or Kobo SQLite path, or press Esc to cancel.", isError: true);
            return ClippingsImportOutcome.Cancelled();
        }

        var hadBooksView = _viewHasBooksList;
        var outcome = await _syncWorkflow.ExecuteAsync(new ClippingsImportOptions
        {
            FilePath = resolvedPath
        }, cancellationToken).ConfigureAwait(false);

        switch (outcome.Status)
        {
            case ClippingsImportStatus.FileNotFound:
                SetFeedback($"File not found: {outcome.FilePath}", isError: true);
                return outcome;

            case ClippingsImportStatus.ParseFailed:
                SetFeedback($"Error parsing clippings file: {outcome.Message}", isError: true);
                return outcome;

            case ClippingsImportStatus.ServerError:
                _onConnectionFailure?.Invoke();
                SetFeedback($"Import failed: {outcome.Message}", isError: true);
                return outcome;

            case ClippingsImportStatus.NoHighlightsFound:
                SetFeedback("No highlights found in the clippings file.", isError: false);
                CancelImportPrompt();
                return outcome;

            case ClippingsImportStatus.Succeeded:
                await InitializeAsync(cancellationToken).ConfigureAwait(false);
                SetFeedback(
                    $"Import complete. {outcome.TotalHighlightsParsed} parsed, {outcome.Response!.NewHighlights} new, {outcome.Response.DuplicateHighlights} duplicates, {outcome.Response.NewBooks} books, {outcome.Response.NewAuthors} authors.",
                    isError: false);
                CancelImportPrompt();

                if (_navigate is not null)
                {
                    if (hadBooksView || _filteredBooks.Count == 0)
                    {
                        _refreshVisibleBooks?.Invoke();
                    }
                    else
                    {
                        _navigate(ScreenResult.Reload());
                    }
                }
                else
                {
                    ApplyFilter();
                }

                return outcome;

            default:
                return outcome;
        }
    }

    public BookViewModel? GetSelectedBook()
    {
        if (_filteredBooks.Count == 0)
        {
            return null;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _filteredBooks.Count - 1);
        return _filteredBooks[_selectedIndex];
    }

    private void SetupContainerKeyBindings(
        View container,
        ShortcutListView? listView,
        Action<ScreenResult> navigate,
        Action? refreshVisibleBooks,
        Action? focusSearchField,
        Action? focusSyncField)
    {
        void HandleShortcutKey(Key key)
        {
            if (key.Handled)
            {
                return;
            }

            var shortcutKey = GetShortcutKey(key);
            if (shortcutKey is null)
            {
                return;
            }

            if (TryHandleShortcutKey(shortcutKey.Value, navigate, refreshVisibleBooks, focusSearchField, focusSyncField))
            {
                key.Handled = true;
            }
        }

        container.KeyDown += (_, key) => HandleShortcutKey(key);

        if (listView is not null)
        {
            listView.ShortcutKeyPressed += (_, key) => HandleShortcutKey(key);

            listView.ValueChanged += (_, _) =>
            {
                _selectedIndex = listView.SelectedItem ?? 0;
            };
        }
    }

    public bool TryHandleShortcutKey(
        char shortcutKey,
        Action<ScreenResult> navigate,
        Action? refreshVisibleBooks,
        Action? focusSearchField,
        Action? focusSyncField)
    {
        if (_isRenamePromptActive)
        {
            return false;
        }

        switch (char.ToLowerInvariant(shortcutKey))
        {
            case 'q':
                navigate(ScreenResult.ConfirmQuit());
                return true;

            case 's':
                navigate(ScreenResult.Push(new SettingsScreen(_client, refreshChromeAsync: _refreshConnectionStatusAsync)));
                return true;

            case 'i':
                focusSyncField?.Invoke();
                return true;

            case 'n':
                var bookToRename = GetSelectedBook();
                if (bookToRename is not null)
                    BeginRenamePrompt(bookToRename);
                return true;

            case 'r':
                var hadBooksView = _viewHasBooksList;
                InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (hadBooksView || _filteredBooks.Count == 0)
                {
                    refreshVisibleBooks?.Invoke();
                }
                else
                {
                    navigate(ScreenResult.Reload());
                }

                return true;

            case '/':
                focusSearchField?.Invoke();
                return true;

            default:
                return false;
        }
    }

    private static char? GetShortcutKey(Key key)
    {
        return key.AsRune.Value switch
        {
            >= 'a' and <= 'z' => (char)key.AsRune.Value,
            >= 'A' and <= 'Z' => char.ToLowerInvariant((char)key.AsRune.Value),
            '/' => '/',
            _ => key.KeyCode switch
            {
                KeyCode.Q => 'q',
                KeyCode.R => 'r',
                KeyCode.S => 's',
                _ => null
            }
        };
    }

    private static TableLayout CalculateTableLayout(int availableWidth)
    {
        const int SpacingWidth = 2;

        var textColumnsWidth = Math.Max(0, availableWidth - HighlightsColumnWidth - SpacingWidth);
        if (textColumnsWidth == 0)
        {
            return new TableLayout(0, 0, HighlightsColumnWidth);
        }

        var authorWidth = Math.Min(
            PreferredAuthorColumnWidth,
            Math.Max(MinimumAuthorColumnWidth, textColumnsWidth / 3));

        var titleWidth = Math.Max(MinimumTitleColumnWidth, textColumnsWidth - authorWidth);
        if (titleWidth + authorWidth > textColumnsWidth)
        {
            authorWidth = Math.Max(0, textColumnsWidth - titleWidth);
        }

        if (textColumnsWidth >= MinimumTitleColumnWidth + MinimumAuthorColumnWidth)
        {
            authorWidth = Math.Max(MinimumAuthorColumnWidth, authorWidth);
            titleWidth = textColumnsWidth - authorWidth;
        }
        else
        {
            titleWidth = Math.Max(0, textColumnsWidth / 2);
            authorWidth = Math.Max(0, textColumnsWidth - titleWidth);
        }

        return new TableLayout(titleWidth, authorWidth, HighlightsColumnWidth);
    }

    private static string FormatHeader(TableLayout tableLayout)
    {
        return $"{FitCell("TITLE", tableLayout.TitleWidth)} {FitCell("AUTHOR", tableLayout.AuthorWidth)} {"HIGHLIGHTS".PadLeft(tableLayout.HighlightsWidth)}";
    }

    private static string FormatBookRow(BookViewModel book, TableLayout tableLayout)
    {
        var title = FitCell(book.Title, tableLayout.TitleWidth);
        var author = FitCell(book.Author, tableLayout.AuthorWidth);
        return $"{title} {author} {book.HighlightCount.ToString(CultureInfo.InvariantCulture).PadLeft(tableLayout.HighlightsWidth)}";
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

    private async Task<List<HighlightItemDto>> LoadAllHighlightsAsync(CancellationToken cancellationToken)
    {
        var highlights = new List<HighlightItemDto>();
        var page = 1;

        while (true)
        {
            var response = await _client.GetHighlightsAsync(page, PageSize, null, cancellationToken).ConfigureAwait(false);
            if (response.Items.Count == 0)
            {
                break;
            }

            highlights.AddRange(response.Items);
            if (highlights.Count >= response.Total)
            {
                break;
            }

            page++;
        }

        return highlights;
    }

    private static List<BookViewModel> EnrichBooks(
        IEnumerable<BookViewModel> books,
        ExclusionsResponse exclusions,
        IEnumerable<WeightedHighlightDto> weights)
    {
        var excludedHighlightIds = exclusions.Highlights.Select(highlight => highlight.Id).ToHashSet();
        var excludedBookIds = exclusions.Books
            .Select(book => book.Id)
            .ToHashSet();
        var excludedAuthorIds = exclusions.Authors
            .Select(author => author.Id)
            .ToHashSet();
        var weightLookup = weights.ToDictionary(weight => weight.Id, weight => weight.Weight);

        return books
            .Select(book =>
            {
                var isBookExcluded = excludedBookIds.Contains(book.BookId);
                var isAuthorExcluded = excludedAuthorIds.Contains(book.AuthorId);
                var highlights = book.Highlights
                    .Select(highlight => new HighlightViewModel(
                        highlight.Id,
                        highlight.BookId,
                        highlight.AuthorId,
                        highlight.Text,
                        highlight.BookTitle,
                        highlight.AuthorName,
                        excludedHighlightIds.Contains(highlight.Id),
                        weightLookup.TryGetValue(highlight.Id, out var weight) ? weight : null))
                    .ToList();

                return new BookViewModel(
                    book.BookId,
                    book.AuthorId,
                    book.Title,
                    book.Author,
                    highlights.Count,
                    isBookExcluded,
                    isAuthorExcluded,
                    highlights);
            })
            .ToList();
    }

    private void ApplyFilter()
    {
        _filteredBooks = string.IsNullOrEmpty(_searchQuery)
            ? [.. _books]
            : SearchFilter.Apply(_books, _searchQuery);

        if (_filteredBooks.Count == 0)
        {
            _selectedIndex = 0;
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex, 0, _filteredBooks.Count - 1);
    }

    private void BeginSearchInput()
    {
        _toolbarMode = ToolbarMode.Search;
        _isSearchActive = true;

        if (_searchField is not null)
        {
            _searchField.Text = _searchQuery;
            _searchField.SetFocus();
            _searchField.MoveEnd();
        }

        UpdateToolbarChrome();
    }

    private void LeaveSearchInput(Action? refreshVisibleBooks)
    {
        _toolbarMode = ToolbarMode.Search;
        _isSearchActive = false;

        if (_searchField is not null)
        {
            _searchQuery = _searchField.Text ?? string.Empty;
        }

        ApplyFilter();
        refreshVisibleBooks?.Invoke();
        UpdateToolbarChrome();
    }

    private void HandleToolbarTextChanged()
    {
        if (_searchField is null)
        {
            return;
        }

        if (_toolbarMode == ToolbarMode.SyncPath)
        {
            _syncPathInput = _searchField.Text ?? string.Empty;
        }
        else
        {
            _searchQuery = _searchField.Text ?? string.Empty;
            ApplyFilter();
            _refreshVisibleBooks?.Invoke();
        }

        UpdateToolbarChrome();
    }

    private void HandleToolbarKeyDown(Key key)
    {
        if (_toolbarMode == ToolbarMode.SyncPath)
        {
            if (key.KeyCode == KeyCode.Esc)
            {
                CancelImportPrompt();
                key.Handled = true;
            }

            return;
        }

        if (key.KeyCode is KeyCode.Esc or KeyCode.CursorDown)
        {
            LeaveSearchInput(_refreshVisibleBooks);
            _listView?.SetFocus();
            key.Handled = true;
        }
    }

    private async Task HandleToolbarSubmitAsync()
    {
        if (_toolbarMode != ToolbarMode.SyncPath)
        {
            return;
        }

        await SubmitImportAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
    }

    private void HandleRenamePromptKeyDown(Key key)
    {
        if (key.KeyCode == KeyCode.Esc)
        {
            CancelRenamePrompt();
            key.Handled = true;
            return;
        }

        if (key.KeyCode == KeyCode.Tab)
        {
            key.Handled = true;
        }
    }

    private string GetToolbarText() => _toolbarMode switch
    {
        ToolbarMode.SyncPath => _syncPathInput,
        _ => _searchQuery
    };

    private string GetToolbarPlaceholder(bool hasFocus)
    {
        if (_toolbarMode == ToolbarMode.SyncPath)
        {
            return _hasDetectedSyncPath
                ? SyncPlaceholderDetected
                : SyncPlaceholderManual;
        }

        return hasFocus ? SearchPlaceholderFocused : SearchPlaceholderIdle;
    }

    private static string? ResolveDefaultSyncPath(string? detectedPath)
    {
        return !string.IsNullOrWhiteSpace(detectedPath)
            ? detectedPath
            : KindleDetector.GetSuggestedClippingsPath();
    }

    private void UpdateToolbarChrome()
    {
        if (_searchField is null || _searchPlaceholder is null || _searchFrame is null)
        {
            return;
        }

        _searchFrame.Title = _toolbarMode switch
        {
            ToolbarMode.SyncPath => " Import ",
            _ => string.Empty
        };
        _searchPlaceholder.Visible = string.IsNullOrEmpty(_searchField.Text);
        _searchPlaceholder.Text = GetToolbarPlaceholder(_searchField.HasFocus);
        _searchField.CanFocus = !_isRenamePromptActive;
        _searchFrame.CanFocus = !_isRenamePromptActive;
        _searchFrame.SetScheme(CreateSearchFrameScheme(_searchField.HasFocus));
    }

    private void UpdateRenamePromptState()
    {
        ModalChrome.SetBackdropVisible(_renameBackdrop, _isRenamePromptActive);

        if (_renameFrame is not null)
        {
            var isFocused = _isRenamePromptActive && ((_renameField?.HasFocus ?? false) || _renameFrame.HasFocus);
            _renameFrame.Visible = _isRenamePromptActive;
            _renameFrame.SetScheme(ModalChrome.CreateFrameScheme(isFocused));
        }

        if (_listView is not null)
        {
            _listView.CanFocus = !_isRenamePromptActive;
        }

        UpdateToolbarChrome();

        if (_isRenamePromptActive)
        {
            _renameFrame?.SetFocus();
            _renameField?.SetFocus();
            _renameField?.MoveEnd();
        }
        else if (_listView is not null)
        {
            _listView.SetFocus();
        }
        else
        {
            _searchField?.SetFocus();
        }
    }

    private void ShowRenamePromptView()
    {
        if (_renameField is not null)
        {
            _renameField.Text = _renameInput;
        }

        UpdateRenamePromptState();
    }

    private void HideRenamePromptView()
    {
        if (_renameField is not null)
        {
            _renameField.Text = string.Empty;
        }

        UpdateRenamePromptState();
    }

    private void NotifyUiStateChanged()
    {
        _uiStateObserver?.Invoke();
    }

    private Label CreateToolbarFeedbackLabel()
    {
        var feedbackLabel = new Label
        {
            X = 2,
            Y = 3,
            Width = Dim.Fill(3),
            Height = 1,
            Visible = !string.IsNullOrWhiteSpace(_feedbackMessage),
            Text = _feedbackMessage ?? string.Empty,
            CanFocus = false
        };

        ApplyFeedbackLabelState(feedbackLabel);
        return feedbackLabel;
    }

    private void SetFeedback(string message, bool isError)
    {
        _feedbackMessage = message;
        _feedbackIsError = isError;
        UpdateFeedbackLabel();
    }

    private void UpdateFeedbackLabel()
    {
        if (_feedbackLabel is null)
        {
            return;
        }

        ApplyFeedbackLabelState(_feedbackLabel);
    }

    private void ApplyFeedbackLabelState(Label feedbackLabel)
    {
        feedbackLabel.Text = _feedbackMessage ?? string.Empty;
        feedbackLabel.Visible = !string.IsNullOrWhiteSpace(_feedbackMessage);
        var palette = TuiTheme.Palette;
        feedbackLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
            _feedbackIsError ? palette.Error : palette.TextMuted,
            palette.Background)));
    }

}
