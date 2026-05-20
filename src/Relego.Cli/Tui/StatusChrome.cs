using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Relego.Cli.Infrastructure;

namespace Relego.Cli.Tui;

public sealed class StatusChrome(string serverUrl, string version)
{
    private static readonly string[] LogoLines =
    [
        "██████╗  ███████╗ ██╗      ███████╗   ██████╗   ██████╗ ",
        "██╔══██╗ ██╔════╝ ██║      ██╔════╝  ██╔════╝  ██╔═══██╗",
        "██████╔╝ █████╗   ██║      █████╗    ██║  ███╗ ██║   ██║",
        "██╔══██╗ ██╔══╝   ██║      ██╔══╝    ██║   ██║ ██║   ██║",
        "██║  ██║ ███████╗ ███████╗ ███████╗  ╚██████╔╝ ╚██████╔╝",
        "╚═╝  ╚═╝ ╚══════╝ ╚══════╝ ╚══════╝   ╚═════╝   ╚═════╝ ",
    ];

    public static Color Background => TuiTheme.Palette.Background;

    public const int LogoHeight = 7; // 6 logo + separator

    private Label? _connectionLabel;
    private Label? _warningLabel;

    public bool IsConnected { get; private set; }
    public bool KindleEmailConfigured { get; private set; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the parent container hierarchy")]
    public View CreateView()
    {
        var palette = TuiTheme.Palette;
        var bg = palette.Background;

        var container = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = LogoHeight
        };
        container.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Text, bg)));

        for (var i = 0; i < LogoLines.Length; i++)
        {
            var logoLabel = new Label
            {
                Text = LogoLines[i],
                X = 1,
                Y = i,
                Width = Dim.Fill(1)
            };
            logoLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.AccentText, bg)));
            container.Add(logoLabel);
        }

        var separatorLabel = new Label
        {
            Text = new string('─', 300),
            X = 0,
            Y = LogoLines.Length,
            Width = Dim.Fill()
        };
        separatorLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Border, bg)));
        container.Add(separatorLabel);

        var longestContent = $"⟳ Checking...  {serverUrl}";
        var frameContentWidth = longestContent.Length + 4;

        var infoFrame = new FrameView
        {
            Title = string.Empty,
            X = Pos.AnchorEnd(frameContentWidth),
            Y = 1,
            Width = frameContentWidth,
            Height = 5,
            BorderStyle = LineStyle.Rounded
        };
        infoFrame.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Border, bg)));

        _connectionLabel = new Label
        {
            Text = $"⟳ Checking...  {serverUrl}",
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            TextAlignment = Alignment.End
        };

        _warningLabel = new Label
        {
            Text = string.Empty,
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            TextAlignment = Alignment.End
        };

        var versionLabel = new Label
        {
            Text = $"relego v{version}",
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            TextAlignment = Alignment.End
        };
        versionLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.TextMuted, bg)));

        infoFrame.Add(_connectionLabel, _warningLabel, versionLabel);
        container.Add(infoFrame);

        return container;
    }

    public void SetDisconnected()
    {
        IsConnected = false;
        KindleEmailConfigured = false;
    }

    public async Task RefreshAsync(RelegoHttpClient client, CancellationToken ct = default)
    {
        try
        {
            IsConnected = await client.PingAsync(ct).ConfigureAwait(false);
            if (IsConnected)
            {
                var settings = await client.GetSettingsAsync(ct).ConfigureAwait(false);
                KindleEmailConfigured = !string.IsNullOrWhiteSpace(settings.KindleEmail);
            }
            else
            {
                KindleEmailConfigured = false;
            }
        }
        catch (HttpRequestException)
        {
            IsConnected = false;
            KindleEmailConfigured = false;
        }
    }

    public void UpdateLabels()
    {
        var palette = TuiTheme.Palette;
        var bg = palette.Background;

        if (_connectionLabel is not null)
        {
            _connectionLabel.Text = IsConnected
                ? $"● Connected to {serverUrl}"
                : $"● Disconnected {serverUrl}";

            _connectionLabel.SetScheme(IsConnected
                ? new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Success, bg))
                : new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Error, bg)));
        }

        if (_warningLabel is not null)
        {
            if (KindleEmailConfigured)
            {
                _warningLabel.Text = string.Empty;
            }
            else
            {
                _warningLabel.Text = "⚠ Kindle email not configured";
                _warningLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(palette.Warning, bg)));
            }
        }
    }
}
