using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Relego.Cli.Infrastructure;
using Relego.Cli.Import;

namespace Relego.Cli.Tui;

public sealed class TuiApp(RelegoHttpClient client, ClippingsImportWorkflow syncWorkflow, string serverUrl, string version)
{
    private const int SplashTopPadding = 1;
    private const int ExitConfirmationWidth = 60;
    private const int ExitConfirmationHeight = 6;

    private static readonly IReadOnlyList<(string Key, string Label)> ExitConfirmationHints =
    [
        ("Y", "Confirm"),
        ("N", "Cancel"),
        ("Esc", "Cancel")
    ];

    private static readonly string[] SplashLines =
    [
        "██████╗  ███████╗ ██╗      ███████╗   ██████╗   ██████╗ ",
        "██╔══██╗ ██╔════╝ ██║      ██╔════╝  ██╔════╝  ██╔═══██╗",
        "██████╔╝ █████╗   ██║      █████╗    ██║  ███╗ ██║   ██║",
        "██╔══██╗ ██╔══╝   ██║      ██╔══╝    ██║   ██║ ██║   ██║",
        "██║  ██║ ███████╗ ███████╗ ███████╗  ╚██████╔╝ ╚██████╔╝",
        "╚═╝  ╚═╝ ╚══════╝ ╚══════╝ ╚══════╝   ╚═════╝   ╚═════╝ ",
    ];

    private readonly RelegoHttpClient _client = client;
    private readonly ClippingsImportWorkflow _syncWorkflow = syncWorkflow;
    private readonly StatusChrome _chrome = new(serverUrl, version);
    private readonly Stack<IScreen> _screens = new();
    private IApplication? _app;
    private FrameView? _contentFrame;
    private View? _toolbarView;
    private View? _statusBar;
    private Window? _window;
    private Label? _exitConfirmationBackdrop;
    private FrameView? _exitConfirmationFrame;
    private bool _isExitConfirmationOpen;
    private IReadOnlyList<(string Key, string Label)> _currentHints = [];

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_screens.Count == 0)
        {
            var initialScreen = new BookListScreen(_client, _syncWorkflow, HandleConnectionFailure, RefreshChromeAsync);
            await initialScreen.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _screens.Push(initialScreen);
        }

        _app = Application.Create();
        _app.Init();

        try
        {
            var cols = Console.IsOutputRedirected ? 80 : Console.WindowWidth;
            var rows = Console.IsOutputRedirected ? 24 : Console.WindowHeight;
            if (cols < 80 || rows < 24)
            {
                await ShowTooSmallAsync().ConfigureAwait(false);
                return;
            }

            await RunSplashAsync(cancellationToken).ConfigureAwait(false);

            _window = new Window
            {
                Title = "Relego",
                BorderStyle = LineStyle.None
            };
            _window.Padding.Thickness = new Thickness(1);
            var palette = TuiTheme.Palette;
            _window.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Text, palette.Background)));

            using var headerView = _chrome.CreateView();
            _window.Add(headerView);

            _contentFrame = new FrameView
            {
                X = 0,
                Y = StatusChrome.LogoHeight,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
                Title = _screens.Peek().Title,
                CanFocus = true
            };
            _contentFrame.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Border, palette.Background)));
            _window.Add(_contentFrame);

            _statusBar = new View
            {
                X = 0,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill(),
                Height = 1,
                CanFocus = false
            };
            _statusBar.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.TextMuted, palette.Background)));
            _window.Add(_statusBar);

            _window.KeyDown += (_, key) =>
            {
                if (HandleExitConfirmationKey(key))
                {
                    key.Handled = true;
                }
            };

            ShowCurrentScreen();

            _ = Task.Run(() => RefreshChromeAsync(cancellationToken));

            _app.Run(_window);
        }
        finally
        {
            _window?.Dispose();
            (_app as IDisposable)?.Dispose();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the splash window")]
    private Task ShowTooSmallAsync()
    {
        if (_app is null)
        {
            return Task.CompletedTask;
        }

        using var win = new Window { BorderStyle = LineStyle.None };
        var palette = TuiTheme.Palette;
        win.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Text, palette.Background)));

        var line1 = new Label { Text = "Terminal too small.", X = Pos.Center(), Y = Pos.AnchorEnd(4) };
        var line2 = new Label { Text = "Please resize to at least 80×24 and restart.", X = Pos.Center(), Y = Pos.AnchorEnd(3) };
        var line3 = new Label { Text = "Press any key to exit.", X = Pos.Center(), Y = Pos.AnchorEnd(2) };
        line3.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.TextMuted, palette.Background)));
        win.Add(line1, line2, line3);
        win.KeyDown += (_, _) => _app!.RequestStop();
        _app.Run(win);
        return Task.CompletedTask;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the splash window")]
    private async Task RunSplashAsync(CancellationToken ct)
    {
        if (_app is null)
        {
            return;
        }

        using var splashWindow = new Window
        {
            BorderStyle = LineStyle.None
        };
        var palette = TuiTheme.Palette;
        splashWindow.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Text, palette.Background)));

        var maxWidth = SplashLines.Max(line => line.Length);
        var splashContentWidth = maxWidth;

        var splashContent = new View
        {
            X = 0,
            Y = SplashTopPadding,
            Width = splashContentWidth,
            Height = SplashLines.Length + 2,
            CanFocus = false
        };
        splashWindow.Add(splashContent);

        void CenterSplashContent()
        {
            var viewportWidth = splashWindow.Viewport.Width;
            if (viewportWidth <= 0)
            {
                return;
            }

            splashContent.X = Math.Max(0, (viewportWidth - splashContentWidth) / 2);
        }

        splashWindow.ViewportChanged += (_, _) => CenterSplashContent();

        var labels = new Label[SplashLines.Length];
        for (var i = 0; i < SplashLines.Length; i++)
        {
            labels[i] = new Label
            {
                Text = string.Empty,
                X = 0,
                Y = i,
                Width = splashContentWidth,
                TextAlignment = Alignment.Center
            };
            labels[i].SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.SplashLineColors[i], palette.Background)));
            splashContent.Add(labels[i]);
        }

        var versionLabel = new Label
        {
            Text = string.Empty,
            X = 0,
            Y = SplashLines.Length + 1,
            Width = splashContentWidth,
            TextAlignment = Alignment.Center
        };
        versionLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.TextMuted, palette.Background)));
        splashContent.Add(versionLabel);

        var token = _app.Begin(splashWindow);

        try
        {
            CenterSplashContent();
            _app.LayoutAndDraw(true);

            for (var width = 2; width <= maxWidth; width += 2)
            {
                ct.ThrowIfCancellationRequested();
                var currentWidth = Math.Min(width, maxWidth);

                for (var i = 0; i < SplashLines.Length; i++)
                {
                    var line = SplashLines[i];
                    labels[i].Text = line[..Math.Min(currentWidth, line.Length)];
                }

                _app.LayoutAndDraw(true);
                await Task.Delay(40, ct).ConfigureAwait(false);
            }

            for (var i = 0; i < SplashLines.Length; i++)
            {
                labels[i].Text = SplashLines[i];
            }

            _app.LayoutAndDraw(true);

            versionLabel.Text = $"v{version}";
            _app.LayoutAndDraw(true);
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (token is not null)
            {
                _app.End(token);
            }
        }
    }

    private void Navigate(ScreenResult result)
    {
        switch (result.Action)
        {
            case ScreenAction.None:
                return;
            case ScreenAction.Reload:
                ShowCurrentScreen();
                return;
            case ScreenAction.ConfirmQuit:
                OpenExitConfirmation();
                return;
            case ScreenAction.Push when result.Next is not null:
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await result.Next.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (HttpRequestException)
                    {
                        HandleConnectionFailure();
                    }

                    _app?.Invoke(() =>
                    {
                        _screens.Push(result.Next);
                        ShowCurrentScreen();
                    });
                });
                break;
            case ScreenAction.Pop:
                if (_screens.Count > 1)
                {
                    _screens.Pop();
                    ShowCurrentScreen();
                }

                break;
            case ScreenAction.Quit:
                _app?.RequestStop();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(result.Action), result.Action, null);
        }
    }

    private void HandleConnectionFailure()
    {
        _chrome.SetDisconnected();
        _app?.Invoke(() => _chrome.UpdateLabels());
    }

    private async Task RefreshChromeAsync(CancellationToken cancellationToken)
    {
        await _chrome.RefreshAsync(_client, cancellationToken).ConfigureAwait(false);
        _app?.Invoke(() => _chrome.UpdateLabels());
    }

    private void ShowCurrentScreen()
    {
        if (_screens.Count == 0 || _contentFrame is null || _window is null)
        {
            return;
        }

        var screen = _screens.Peek();

        if (_toolbarView is not null)
        {
            _window.Remove(_toolbarView);
            _toolbarView = null;
        }

        var toolbarHeight = screen.ToolbarHeight;
        _toolbarView = screen.CreateToolbarView(Navigate);

        if (_toolbarView is not null && toolbarHeight > 0)
        {
            _toolbarView.X = 0;
            _toolbarView.Y = StatusChrome.LogoHeight;
            _toolbarView.Width = Dim.Fill();
            _toolbarView.Height = toolbarHeight;
            _window.Add(_toolbarView);
        }
        else
        {
            toolbarHeight = 0;
        }

        _contentFrame.Y = StatusChrome.LogoHeight + toolbarHeight;

        _contentFrame.RemoveAll();
        _contentFrame.Title = screen.Title;

        screen.RegisterUiStateObserver(() => _app?.Invoke(() => RefreshCurrentHints(screen)));

        var view = screen.CreateView(Navigate);
        _contentFrame.Add(view);
        view.SetFocus();

        RefreshCurrentHints(screen);
    }

    private void RefreshCurrentHints(IScreen screen)
    {
        _currentHints = screen.KeyHints;
        UpdateStatusBar(_isExitConfirmationOpen ? ExitConfirmationHints : _currentHints);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the window hierarchy")]
    private void EnsureExitConfirmationView()
    {
        if (_window is null || _exitConfirmationFrame is not null)
        {
            return;
        }

        _exitConfirmationBackdrop = ModalChrome.CreateBackdrop();

        _exitConfirmationFrame = ModalChrome.CreateFrame(ExitConfirmationWidth, ExitConfirmationHeight, "Confirm Exit");

        var message = new Label
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(2),
            Height = 2,
            Text = "Exit Relego? Press Y to confirm or N/Esc to cancel.",
            CanFocus = false
        };
        message.SetScheme(ModalChrome.CreateBodyTextScheme());

        _exitConfirmationFrame.Add(message);
        _window.Add(_exitConfirmationBackdrop);
        _window.Add(_exitConfirmationFrame);
    }

    private void OpenExitConfirmation()
    {
        if (_isExitConfirmationOpen)
        {
            return;
        }

        EnsureExitConfirmationView();
        if (_exitConfirmationFrame is null)
        {
            return;
        }

        _isExitConfirmationOpen = true;
        ModalChrome.SetBackdropVisible(_exitConfirmationBackdrop, visible: true);
        _exitConfirmationFrame.Visible = true;
        _exitConfirmationFrame.SetFocus();
        UpdateStatusBar(ExitConfirmationHints);
    }

    private void CloseExitConfirmation()
    {
        if (!_isExitConfirmationOpen)
        {
            return;
        }

        _isExitConfirmationOpen = false;
        ModalChrome.SetBackdropVisible(_exitConfirmationBackdrop, visible: false);
        if (_exitConfirmationFrame is not null)
        {
            _exitConfirmationFrame.Visible = false;
        }

        if (_contentFrame?.SubViews.FirstOrDefault() is { } currentView)
        {
            currentView.SetFocus();
        }

        UpdateStatusBar(_currentHints);
    }

    private bool HandleExitConfirmationKey(Key key)
    {
        if (!_isExitConfirmationOpen)
        {
            return false;
        }

        var rune = key.AsRune.Value;
        var confirm = rune is 'y' or 'Y' || key.KeyCode == KeyCode.Enter;
        var cancel = rune is 'n' or 'N' || key.KeyCode == KeyCode.Esc;

        if (confirm)
        {
            _app?.RequestStop();
            return true;
        }

        if (cancel)
        {
            CloseExitConfirmation();
            return true;
        }

        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Labels are owned by the footer view")]
    private void UpdateStatusBar(IReadOnlyList<(string Key, string Label)> hints)
    {
        if (_statusBar is null)
        {
            return;
        }

        var palette = TuiTheme.Palette;

        _statusBar.RemoveAll();

        var cursorX = 1;

        foreach (var (key, label) in hints)
        {
            if (string.Equals(key, "Ctrl+C", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var keyText = key.Contains('↑') || key.Contains('↓')
                ? key
                : $"<{key}>";

            var keyLabel = new Label
            {
                X = cursorX,
                Y = 0,
                Width = keyText.Length,
                Height = 1,
                Text = keyText,
                CanFocus = false
            };
            keyLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.AccentText, palette.Background)));
            _statusBar.Add(keyLabel);
            cursorX += keyText.Length;

            if (!string.IsNullOrWhiteSpace(label) && !(key.Contains('↑') || key.Contains('↓')))
            {
                var labelText = $" {label}";
                var descriptionLabel = new Label
                {
                    X = cursorX,
                    Y = 0,
                    Width = labelText.Length,
                    Height = 1,
                    Text = labelText,
                    CanFocus = false
                };
                descriptionLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.TextMuted, palette.Background)));
                _statusBar.Add(descriptionLabel);
                cursorX += labelText.Length;
            }

            cursorX += 2;
        }
    }
}
